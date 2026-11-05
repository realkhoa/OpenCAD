using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using OpenCad.Core.Editing;
using ACadSharp.Tables;
using CSMath;

namespace OpenCad.Core.Scripting;

/// <summary>Kết quả chạy một script.</summary>
public sealed record ScriptResult(int Executed, int Failed, List<string> Errors)
{
    public bool Ok => Failed == 0;
}

/// <summary>
/// Chạy file lệnh dạng văn bản để sinh bản vẽ mà không cần giao diện.
/// Mỗi dòng một lệnh; dòng bắt đầu bằng # là chú thích.
///
/// Ví dụ:
///   LAYER TUONG 6
///   LINE 0,0 8000,0
///   RECT 0,0 8000,6000
///   CIRCLE 4000,3000 500
///   TEXT 1000,3000 250 PHONG KHACH
/// </summary>
public static class ScriptRunner
{
    /// <summary>Chạy script trên một workspace (bản vẽ đã mở hoặc mới).</summary>
    public static ScriptResult Run(CadWorkspace workspace, string script)
    {
        var errors = new List<string>();
        var executed = 0;
        var failed = 0;

        if (workspace is null) return new ScriptResult(0, 1, new List<string> { "workspace null" });

        var document = workspace.Document;
        var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lineNumber = 0;
        Layer? current = document.Layers.FirstOrDefault(l => l.Name == "0")
                         ?? document.Layers.FirstOrDefault();
        BlockRecord? currentBlock = null;
        var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            lineNumber++;

            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            try
            {
                // Bỏ chú thích cuối dòng (# ...)
                var hashIndex = line.IndexOf(" #", StringComparison.Ordinal);

                if (hashIndex > 0) line = line[..hashIndex].Trim();

                // Thay $tên bằng giá trị trước khi phân tích (áp cho cả SET)
                line = ExpandVariables(line, variables, out var missing);

                if (missing is not null)
                {
                    failed++;
                    errors.Add($"Dòng {lineNumber}: biến ${missing} chưa được đặt");
                    continue;
                }

                // Biến: SET tên giá_trị
                if (line.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
                {
                    var setParts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    if (setParts.Length >= 3 &&
                        double.TryParse(setParts[2].Replace(',', '.'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var value))
                    {
                        variables[setParts[1]] = value;
                        executed++;
                        continue;
                    }

                    failed++;
                    errors.Add($"Dòng {lineNumber}: SET cần tên và giá trị số");
                    continue;
                }

                if (Execute(document, line, ref current, ref currentBlock)) executed++;
                else { failed++; errors.Add($"Dòng {lineNumber}: không hiểu lệnh \"{line}\""); }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Dòng {lineNumber}: {ex.GetType().Name} — {ex.Message}");
            }
        }

        return new ScriptResult(executed, failed, errors);
    }

    /// <summary>Thay <c>$tên</c> bằng giá trị biến; trả về tên biến còn thiếu (nếu có).</summary>
    private static string ExpandVariables(string line, Dictionary<string, double> variables, out string? missing)
    {
        missing = null;

        if (!line.Contains('$', StringComparison.Ordinal)) return line;

        string? missingName = null;

        var expanded = System.Text.RegularExpressions.Regex.Replace(line, @"\$([A-Za-z_]\w*)", match =>
        {
            var name = match.Groups[1].Value;

            if (variables.TryGetValue(name, out var value))
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            missingName ??= name;
            return match.Value;
        });

        missing = missingName;
        return expanded;
    }

    /// <summary>Chạy script và trả về workspace mới.</summary>
    public static (CadWorkspace Workspace, ScriptResult Result) RunNew(string script)
    {
        var workspace = CadWorkspace.New();
        var result = Run(workspace, script);

        return (workspace, result);
    }

    private static bool Execute(CadDocument document, string line,
                                 ref Layer? current, ref BlockRecord? currentBlock)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();

        switch (command)
        {
            case "LAYER":
            {
                if (parts.Length < 2) return false;

                var name = parts[1];
                var color = parts.Length > 2 ? (short)ParseNumber(parts[2]) : (short)7;

                var existing = document.Layers.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    existing = new Layer(name) { Color = new Color(color) };
                    document.Layers.Add(existing);
                }
                else
                {
                    existing.Color = new Color(color);
                }

                current = existing;
                return true;
            }

            case "LINE":
            {
                if (parts.Length < 3) return false;

                var (x1, y1) = ParsePoint(parts[1]);
                var (x2, y2) = ParsePoint(parts[2]);

                Add(document, new Line
                {
                    StartPoint = new XYZ(x1, y1, 0),
                    EndPoint = new XYZ(x2, y2, 0),
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            case "RECT":
            case "RECTANG":
            {
                if (parts.Length < 3) return false;

                var (x1, y1) = ParsePoint(parts[1]);
                var (x2, y2) = ParsePoint(parts[2]);
                var layer = CurrentLayer(document, parts, current);

                var corners = new[]
                {
                    new XY(x1, y1), new XY(x2, y1), new XY(x2, y2), new XY(x1, y2),
                };

                for (var i = 0; i < 4; i++)
                {
                    var next = (i + 1) % 4;

                    Add(document, new Line
                    {
                        StartPoint = new XYZ(corners[i].X, corners[i].Y, 0),
                        EndPoint = new XYZ(corners[next].X, corners[next].Y, 0),
                        Layer = layer,
                    }, currentBlock);
                }

                return true;
            }

            case "CIRCLE":
            {
                if (parts.Length < 3) return false;

                var (cx, cy) = ParsePoint(parts[1]);
                var radius = ParseNumber(parts[2]);

                if (radius <= 0) return false;

                Add(document, new Circle
                {
                    Center = new XYZ(cx, cy, 0),
                    Radius = radius,
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            case "TEXT":
            {
                if (parts.Length < 4) return false;

                var (x, y) = ParsePoint(parts[1]);
                var height = ParseNumber(parts[2]);

                if (height <= 0) return false;

                // Nội dung là phần còn lại của dòng (cho phép khoảng trắng)
                var content = string.Join(" ", parts.Skip(3)).Trim();

                // Bóc nháy đơn/nháy kép nếu người viết script bọc nội dung
                if (content.Length >= 2 &&
                    ((content[0] == '\'' && content[^1] == '\'') ||
                     (content[0] == '"' && content[^1] == '"')))
                    content = content[1..^1];

                Add(document, new TextEntity
                {
                    InsertPoint = new XYZ(x, y, 0),
                    Height = height,
                    Value = content,
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            case "PLINE":
            case "POLYLINE":
            {
                var points = new List<XY>();
                var closed = false;

                foreach (var token in parts.Skip(1))
                {
                    if (token.Equals("CLOSE", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("C", StringComparison.OrdinalIgnoreCase))
                    {
                        closed = true;
                        continue;
                    }

                    var (px, py) = ParsePoint(token);
                    points.Add(new XY(px, py));
                }

                if (points.Count < 2) return false;

                var pline = new LwPolyline
                {
                    IsClosed = closed,
                    Layer = CurrentLayer(document, parts, current),
                };

                foreach (var point in points)
                    pline.Vertices.Add(new LwPolyline.Vertex { Location = point });

                Add(document, pline, currentBlock);
                return true;
            }

            case "DIM":
            case "DIMLINEAR":
            case "DIMALIGNED":
            {
                if (parts.Length < 4) return false;

                var (dx1, dy1) = ParsePoint(parts[1]);
                var (dx2, dy2) = ParsePoint(parts[2]);
                var (dx3, dy3) = ParsePoint(parts[3]);

                Add(document, new DimensionAligned
                {
                    FirstPoint = new XYZ(dx1, dy1, 0),
                    SecondPoint = new XYZ(dx2, dy2, 0),
                    DefinitionPoint = new XYZ(dx3, dy3, 0),
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            case "HATCH":
            {
                if (parts.Length < 3) return false;

                var (hx1, hy1) = ParsePoint(parts[1]);
                var (hx2, hy2) = ParsePoint(parts[2]);

                var boundary = new LwPolyline { IsClosed = true };
                boundary.Vertices.Add(new LwPolyline.Vertex { Location = new XY(hx1, hy1) });
                boundary.Vertices.Add(new LwPolyline.Vertex { Location = new XY(hx2, hy1) });
                boundary.Vertices.Add(new LwPolyline.Vertex { Location = new XY(hx2, hy2) });
                boundary.Vertices.Add(new LwPolyline.Vertex { Location = new XY(hx1, hy2) });

                var hatch = Editing.HatchFactory.FromBoundary(boundary);

                if (hatch is null) return false;

                var layer = CurrentLayer(document, parts, current);
                hatch.Layer = layer;

                // Tham số tuỳ chọn: tên pattern và tỉ lệ
                var patternName = parts.Length > 3 && !double.TryParse(parts[3], out _) ? parts[3] : "ANSI31";
                var scaleIndex = parts.Length > 3 && !double.TryParse(parts[3], out _) ? 4 : 3;

                if (parts.Length > scaleIndex && double.TryParse(parts[scaleIndex], out var hatchScale) && hatchScale > 0)
                    hatch.PatternScale = hatchScale;

                try
                {
                    hatch.IsSolid = false;
                    hatch.Pattern = new HatchPattern(patternName);
                }
                catch { }

                Add(document, hatch, currentBlock);
                return true;
            }

            case "ARC":
            {
                if (parts.Length < 5) return false;

                var (ax, ay) = ParsePoint(parts[1]);
                var arcRadius = ParseNumber(parts[2]);
                var startAngle = ParseNumber(parts[3]) * Math.PI / 180.0;
                var endAngle = ParseNumber(parts[4]) * Math.PI / 180.0;

                if (arcRadius <= 0) return false;

                Add(document, new Arc
                {
                    Center = new XYZ(ax, ay, 0),
                    Radius = arcRadius,
                    StartAngle = startAngle,
                    EndAngle = endAngle,
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            case "BLOCK":
            {
                if (parts.Length < 3) return false;

                var blockName = parts[1];
                var (bx, by) = ParsePoint(parts[2]);

                var block = document.BlockRecords.FirstOrDefault(
                    b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));

                if (block is null)
                {
                    block = new BlockRecord(blockName) { };
                    document.BlockRecords.Add(block);
                }
                else
                {
                }

                currentBlock = block;
                return true;
            }

            case "ENDBLOCK":
                if (currentBlock is null) return false;

                currentBlock = null;
                return true;

            case "INSERT":
            {
                if (parts.Length < 3) return false;

                var blockName = parts[1];
                var (ix, iy) = ParsePoint(parts[2]);

                var block = document.BlockRecords.FirstOrDefault(
                    b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));

                if (block is null) return false;

                var insertScale = parts.Length > 3 && double.TryParse(parts[3], out var parsedScale) && parsedScale > 0
                    ? parsedScale
                    : 1;

                var insertRotation = parts.Length > 4 && double.TryParse(parts[4], out var parsedRotation)
                    ? parsedRotation * Math.PI / 180.0
                    : 0;

                Add(document, new Insert(block)
                {
                    InsertPoint = new XYZ(ix, iy, 0),
                    XScale = insertScale,
                    YScale = insertScale,
                    ZScale = insertScale,
                    Rotation = insertRotation,
                    Layer = CurrentLayer(document, parts, current),
                }, currentBlock);

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Layer cho entity: cho phép ghi đè bằng "ON &lt;tên layer&gt;" ở cuối dòng,
    /// nếu không thì dùng layer hiện hành của script.
    /// </summary>
    private static Layer? CurrentLayer(CadDocument document, string[] parts, Layer? current)
    {
        for (var i = 0; i + 1 < parts.Length; i++)
        {
            if (!parts[i].Equals("ON", StringComparison.OrdinalIgnoreCase)) continue;

            var name = parts[i + 1];

            var explicitLayer = document.Layers.FirstOrDefault(
                l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

            if (explicitLayer is not null) return explicitLayer;
        }

        return current ?? document.Layers.FirstOrDefault();
    }

    /// <summary>Thêm entity vào model space hoặc vào block đang định nghĩa.</summary>
    private static void Add(CadDocument document, Entity entity, BlockRecord? block)
    {
        if (block is not null) block.Entities.Add(entity);
        else document.Entities.Add(entity);
    }

    /// <summary>Thêm entity vào model space hoặc vào block đang định nghĩa.</summary>
    private static (double X, double Y) ParsePoint(string text)
    {
        var numbers = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        if (numbers.Length < 2)
            throw new FormatException($"toạ độ không hợp lệ: {text}");

        return (ParseNumber(numbers[0]), ParseNumber(numbers[1]));
    }

    private static double ParseNumber(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return value;

        throw new FormatException($"số không hợp lệ: {text}");
    }
}
