using OpenCad.Core.Geometry;

namespace OpenCad.Core.Styles;

/// <summary>Một dòng trong định nghĩa pattern hatch (định dạng .pat của AutoCAD).</summary>
public sealed class PatternLine
{
    public double Angle { get; init; }          // độ
    public double OriginX { get; init; }
    public double OriginY { get; init; }
    public double DeltaX { get; init; }         // bước dọc theo đường
    public double DeltaY { get; init; }         // khoảng cách giữa hai đường song song
    public double[] Dashes { get; init; } = Array.Empty<double>();
}

public sealed class HatchPatternDef
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<PatternLine> Lines { get; } = new();
}

/// <summary>
/// Đọc file .pat của AutoCAD và sinh các đoạn thẳng tô cho một vùng kín.
/// Định dạng: dòng đầu <c>*TÊN, mô tả</c>; mỗi dòng sau
/// <c>góc, x, y, dx, dy [, dash1, dash2, ...]</c>.
/// </summary>
public static class HatchPatternLibrary
{
    private static readonly Dictionary<string, HatchPatternDef> Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    static HatchPatternLibrary()
    {
        LoadBuiltIn();
    }

    public static IReadOnlyList<string> Names => Loaded.Keys.OrderBy(k => k).ToList();

    public static HatchPatternDef? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Loaded.TryGetValue(name, out var pattern) ? pattern : null;
    }

    /// <summary>Nạp thêm pattern từ nội dung file .pat. Trả về số pattern đọc được.</summary>
    public static int LoadText(string text)
    {
        var count = 0;

        foreach (var pattern in Parse(text))
        {
            Loaded[pattern.Name] = pattern;
            count++;
        }

        return count;
    }

    public static int LoadFile(string path)
        => File.Exists(path) ? LoadText(File.ReadAllText(path)) : 0;

    /// <summary>Tách nội dung .pat thành danh sách pattern.</summary>
    public static List<HatchPatternDef> Parse(string text)
    {
        var result = new List<HatchPatternDef>();
        HatchPatternDef? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;

            if (line.StartsWith("*", StringComparison.Ordinal))
            {
                var header = line[1..];
                var comma = header.IndexOf(',');
                var name = comma < 0 ? header.Trim() : header[..comma].Trim();
                var description = comma < 0 ? string.Empty : header[(comma + 1)..].Trim();

                if (name.Length == 0) continue;

                current = new HatchPatternDef { Name = name, Description = description };
                result.Add(current);
                continue;
            }

            if (current is null) continue;

            var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .ToList();

            if (parts.Count < 5) continue;

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var angle) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ox) ||
                !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var oy) ||
                !double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dx) ||
                !double.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dy))
                continue;

            var dashes = new List<double>();

            for (var i = 5; i < parts.Count; i++)
                if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dash))
                    dashes.Add(dash);

            current.Lines.Add(new PatternLine
            {
                Angle = angle,
                OriginX = ox,
                OriginY = oy,
                DeltaX = dx,
                DeltaY = dy,
                Dashes = dashes.ToArray(),
            });
        }

        return result;
    }

    /// <summary>
    /// Sinh các đoạn thẳng của pattern bên trong vùng kín <paramref name="loops"/>.
    /// <paramref name="scale"/> giãn pattern, <paramref name="extraAngle"/> xoay thêm (radian).
    /// </summary>
    public static List<Pt[]> Generate(HatchPatternDef pattern, IReadOnlyList<Pt[]> loops,
                                      double scale = 1, double extraAngle = 0,
                                      double spacingFactor = 1, int maxLines = 4000)
    {
        var segments = new List<Pt[]>();

        if (loops.Count == 0) return segments;
        if (scale <= 1e-9) scale = 1;
        if (spacingFactor <= 1e-9) spacingFactor = 1;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in loops.SelectMany(l => l))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        if (minX > maxX || minY > maxY) return segments;

        var diagonal = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        if (diagonal < 1e-9) return segments;

        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;

        var budget = maxLines;

        foreach (var line in pattern.Lines)
        {
            var angle = line.Angle * Math.PI / 180.0 + extraAngle;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            var spacing = Math.Abs(line.DeltaY) * scale * spacingFactor;
            if (spacing < 1e-6) continue;

            var along = Math.Abs(line.DeltaX) * scale;
            if (along < 1e-6) along = spacing;

            // Gốc pattern (đã nhân tỉ lệ) rồi quay quanh tâm vùng
            var ox = line.OriginX * scale;
            var oy = line.OriginY * scale;

            var count = (int)Math.Ceiling(diagonal / spacing) + 2;

            for (var k = -count; k <= count && budget > 0; k++)
            {
                // Điểm gốc của đường thứ k: dịch vuông góc với hướng đường
                var baseX = ox - sin * (k * spacing);
                var baseY = oy + cos * (k * spacing);

                // Dời về gần tâm vùng để không phải duyệt vô hạn
                var shift = ((centerX - baseX) * cos + (centerY - baseY) * sin);

                var startX = baseX + cos * shift;
                var startY = baseY + sin * shift;

                var half = diagonal;
                var ax = startX - cos * half;
                var ay = startY - sin * half;
                var bx = startX + cos * half;
                var by = startY + sin * half;

                var step = Math.Min(along, Math.Max(half / 8, spacing));

                // Cắt theo vùng: chia nhỏ và giữ đoạn có trung điểm nằm trong
                var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (length < 1e-9) continue;

                var pieces = Math.Clamp((int)Math.Ceiling(length / Math.Max(step, 1e-6)), 1, 4000);
                var dx = (bx - ax) / pieces;
                var dy = (by - ay) / pieces;

                Pt? runStart = null;

                for (var i = 0; i < pieces; i++)
                {
                    var p0 = new Pt(ax + dx * i, ay + dy * i);
                    var p1 = new Pt(ax + dx * (i + 1), ay + dy * (i + 1));
                    var mid = new Pt((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);

                    var inside = IsInside(loops, mid);

                    if (inside && runStart is null) runStart = p0;

                    if (!inside && runStart is not null)
                    {
                        segments.Add(new[] { runStart.Value, p0 });
                        runStart = null;
                        budget--;
                    }
                }

                if (runStart is not null)
                {
                    segments.Add(new[] { runStart.Value, new Pt(bx, by) });
                    budget--;
                }
            }
        }

        return segments;
    }

    private static bool IsInside(IReadOnlyList<Pt[]> loops, Pt point)
    {
        var inside = false;

        foreach (var loop in loops)
        {
            if (loop.Length < 3) continue;

            for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
            {
                var pi = loop[i];
                var pj = loop[j];

                if (pi.Y > point.Y != pj.Y > point.Y)
                {
                    var x = (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                    if (point.X < x) inside = !inside;
                }
            }
        }

        return inside;
    }

    // ------------------------------------------------------------ BUILT-IN

    private static void LoadBuiltIn()
    {
        const string builtIn = """
*ANSI31, ANSI Iron, Brick, Stone masonry
45, 0,0, 0,1.5

*ANSI32, ANSI Steel
45, 0,0, 0,3
45, 0.1767767,0.1767767, 0,3

*ANSI37, ANSI Lead, Zinc, Magnesium
45, 0,0, 0,1.5, 1.5,-1.5
135, 0,0, 0,1.5, 1.5,-1.5

*BRICK, Brick masonry
0, 0,0, 0,2, 1,-1
90, 0,0, 1,2, 1,-1

*CONCRETE, Concrete
45, 0,0, 0,1, 2,-1
135, 0,0, 0,1, 2,-1

*EARTH, Earth
0, 0,0, 0,1.5, 1,-0.5
45, 0,0, 0,1.5, 1,-0.5
90, 0,0, 0,1.5, 1,-0.5
135, 0,0, 0,1.5, 1,-0.5

*INSUL, Insulation
0, 0,0, 0,1, 1,-1
0, 0,0.5, 0,1, 1,-1

*STEEL, Steel
45, 0,0, 0,1, 1,-1

*HONEY, Honeycomb
0, 0,0, 1,1, 1,-1
60, 0,0, 1,1, 1,-1
120, 0,0, 1,1, 1,-1
""";

        Parse(builtIn).ForEach(p => Loaded[p.Name] = p);
    }
}
