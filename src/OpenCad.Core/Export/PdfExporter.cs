using System.Text;
using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Export;

public sealed class PdfExportOptions
{
    /// <summary>Khổ giấy tính bằng mm.</summary>
    public double PageWidthMm { get; set; } = 420;    // A3 ngang
    public double PageHeightMm { get; set; } = 297;
    public double MarginMm { get; set; } = 12;
    public bool Landscape { get; set; } = true;
    public string Title { get; set; } = "OpenCAD drawing";
    public bool FitToPage { get; set; } = true;

    /// <summary>
    /// Tỷ lệ in cố định, tính bằng số đơn vị bản vẽ trên 1 mm giấy
    /// (1:50 với bản vẽ mm → 50). Null hoặc 0 = tự căn vừa trang.
    /// </summary>
    public double? UnitsPerMm { get; set; }

    /// <summary>Điểm trên mỗi đơn vị bản vẽ. Chỉ dùng khi FitToPage = false và không có UnitsPerMm.</summary>
    public double PointsPerUnit { get; set; } = 0.05;

    /// <summary>Đường dẫn font TrueType để nhúng (cho chữ có dấu). Null = dùng font chuẩn ASCII.</summary>
    public string? FontPath { get; set; }

    /// <summary>In đơn sắc: mọi nét màu đen, độ đậm lấy theo màu gốc (kiểu CTB).</summary>
    public bool Monochrome { get; set; }

    /// <summary>Vùng bản vẽ cần in. Null = in toàn bộ.</summary>
    public RectD? Area { get; set; }

    /// <summary>Bảng kiểu in theo lớp (thay .ctb). Null = dùng quy tắc mặc định.</summary>
    public PlotStyleTable? PlotStyle { get; set; }

    public PdfExportOptions Clone() => new()
    {
        PageWidthMm = PageWidthMm,
        PageHeightMm = PageHeightMm,
        MarginMm = MarginMm,
        Landscape = Landscape,
        Title = Title,
        FitToPage = FitToPage,
        UnitsPerMm = UnitsPerMm,
        PointsPerUnit = PointsPerUnit,
        FontPath = FontPath,
        Monochrome = Monochrome,
        Area = Area,
        PlotStyle = PlotStyle,
    };

    public double PageWidthPoints => MmToPoints(Landscape ? PageWidthMm : PageHeightMm);
    public double PageHeightPoints => MmToPoints(Landscape ? PageHeightMm : PageWidthMm);

    public static double MmToPoints(double mm) => mm * 72.0 / 25.4;

    /// <summary>
    /// Đổi tỷ lệ 1:N ra số đơn vị bản vẽ trên 1 mm giấy.
    /// <paramref name="mmPerDrawingUnit"/> = 1 nếu bản vẽ vẽ bằng mm, 1000 nếu bằng mét.
    /// </summary>
    public static double FromScaleDenominator(double denominator, double mmPerDrawingUnit = 1)
        => denominator * mmPerDrawingUnit;

    /// <summary>Tìm font hệ thống có hỗ trợ tiếng Việt.</summary>
    public static string? FindSystemFont()
    {
        var candidates = new[]
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\tahoma.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/Library/Fonts/Arial.ttf",
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed record PdfExportResult(double Scale, double UnitsPerMm, RectD Bounds);

/// <summary>Trạng thái nhúng font trong một lần xuất.</summary>
public sealed class FontUsage
{
    public FontUsage(TrueTypeFont font) => Font = font;

    public TrueTypeFont Font { get; }
    public HashSet<ushort> Glyphs { get; } = new();
    public Dictionary<ushort, char> ToUnicode { get; } = new();
}

/// <summary>
/// Xuất bản vẽ ra PDF **vector** — tự sinh cú pháp PDF, không cần thư viện ngoài.
/// Đường nét giữ nguyên vector nên zoom/in không bị vỡ như ảnh raster.
/// </summary>
public static class PdfExporter
{
    private const string FontName = "F1";

    /// <summary>Chiều cao dải tiêu đề chừa ở đỉnh trang (mm).</summary>
    private const double TitleStripMm = 9;

    public static PdfExportResult Export(CadScene scene, string path, PdfExportOptions options,
                                         Func<ACadSharp.Entities.Entity, bool>? visible = null)
    {
        var results = ExportSheets(
            new[] { (scene, options) }, path, options, visible);

        return results[0];
    }

    /// <summary>
    /// Xuất một **tờ in** (paper space) ra PDF. Toạ độ trong tờ đã là mm giấy
    /// nên chỉ cần đổi sang point, không căn lại tỷ lệ.
    /// </summary>
    public static void ExportPaperSheet(Layout.PaperSheet sheet, string path, PdfExportOptions options)
    {
        var font = options.FontPath is { Length: > 0 } ? TrueTypeFont.TryLoad(options.FontPath) : null;
        var usage = font is not null && font.HasCmap && font.HasGlyphs ? new FontUsage(font) : null;

        var sheetOptions = options.Clone();
        sheetOptions.PageWidthMm = sheet.WidthMm;
        sheetOptions.PageHeightMm = sheet.HeightMm;
        sheetOptions.Landscape = true;
        sheetOptions.FitToPage = false;

        var mmToPt = PdfExportOptions.MmToPoints(1);
        var sb = new StringBuilder(32 * 1024);

        sb.Append("q\n");
        sb.Append("1 J 1 j\n");

        foreach (var item in sheet.Items)
        {
            sb.Append("q\n");

            if (item.Clip is { } clip)
            {
                sb.Append($"{N(clip.MinX * mmToPt)} {N(clip.MinY * mmToPt)} " +
                          $"{N(clip.Width * mmToPt)} {N(clip.Height * mmToPt)} re W n\n");
            }

            foreach (var prim in item.Prims)
                AppendPrim(sb, prim, mmToPt, 0, 0, usage, sheetOptions);

            sb.Append("Q\n");
        }

        sb.Append("Q\n");

        var bytes = AssembleMultiPagePdf(
            new[] { (sb.ToString(), sheetOptions) }, options, usage);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Xuất nhiều tờ vào **một** file PDF (batch plot). Mỗi tờ có khổ giấy, tỷ lệ,
    /// vùng in và chế độ màu riêng — giống in cả bộ hồ sơ.
    /// </summary>
    public static IReadOnlyList<PdfExportResult> ExportSheets(
        IReadOnlyList<(CadScene Scene, PdfExportOptions Options)> sheets,
        string path,
        PdfExportOptions baseOptions,
        Func<ACadSharp.Entities.Entity, bool>? visible = null)
    {
        if (sheets.Count == 0)
            throw new ArgumentException("Cần ít nhất một tờ.", nameof(sheets));

        var font = baseOptions.FontPath is { Length: > 0 } ? TrueTypeFont.TryLoad(baseOptions.FontPath) : null;
        var usage = font is not null && font.HasCmap && font.HasGlyphs ? new FontUsage(font) : null;

        var contents = new List<(string Content, PdfExportOptions Options)>(sheets.Count);
        var results = new List<PdfExportResult>(sheets.Count);

        foreach (var (scene, sheetOptions) in sheets)
        {
            var options = sheetOptions.Clone();
            if (options.FontPath is null && usage is not null) options.FontPath = baseOptions.FontPath;

            var (content, result) = BuildContent(scene, options, visible, usage);
            contents.Add((content, options));
            results.Add(result);
        }

        var bytes = AssembleMultiPagePdf(contents, baseOptions, usage);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, bytes);
        return results;
    }

    // ------------------------------------------------------------- CONTENT

    private static (string Content, PdfExportResult Result) BuildContent(
        CadScene scene, PdfExportOptions options,
        Func<ACadSharp.Entities.Entity, bool>? visible, FontUsage? usage)
    {
        var pageW = options.PageWidthPoints;
        var pageH = options.PageHeightPoints;
        var margin = PdfExportOptions.MmToPoints(options.MarginMm);
        var titleStrip = PdfExportOptions.MmToPoints(TitleStripMm);

        var bounds = options.Area ?? scene.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 && bounds.Height <= 0)
            bounds = new RectD(0, 0, 1, 1);

        double scale;
        double offsetX;
        double offsetY;

        // Vùng vẽ thật sự: trừ lề và dải tiêu đề ở đỉnh
        var availableW = Math.Max(1, pageW - 2 * margin);
        var availableH = Math.Max(1, pageH - 2 * margin - titleStrip);

        if (options.FitToPage)
        {
            var sx = bounds.Width > 1e-9 ? availableW / bounds.Width : availableW;
            var sy = bounds.Height > 1e-9 ? availableH / bounds.Height : availableH;

            scale = Math.Min(sx, sy);
            if (scale <= 0) scale = 1;

            var drawW = bounds.Width * scale;
            var drawH = bounds.Height * scale;

            offsetX = margin + (availableW - drawW) / 2 - bounds.MinX * scale;
            offsetY = margin + (availableH - drawH) / 2 - bounds.MinY * scale;
        }
        else if (options.UnitsPerMm is > 0)
        {
            // Tỷ lệ cố định: N đơn vị bản vẽ = 1 mm giấy
            scale = PdfExportOptions.MmToPoints(1) / options.UnitsPerMm.Value;

            var drawW = bounds.Width * scale;
            var drawH = bounds.Height * scale;

            // Căn giữa khổ giấy; nếu bản vẽ lớn hơn trang thì vẫn giữ đúng tỷ lệ (sẽ tràn)
            offsetX = margin + (availableW - drawW) / 2 - bounds.MinX * scale;
            offsetY = margin + (availableH - drawH) / 2 - bounds.MinY * scale;
        }
        else
        {
            scale = options.PointsPerUnit;
            offsetX = margin - bounds.MinX * scale;
            offsetY = margin - bounds.MinY * scale;
        }

        var sb = new StringBuilder(64 * 1024);

        sb.Append("q\n");
        sb.Append("1 J 1 j\n");    // nét tròn hai đầu, nối tròn

        // Nếu in một vùng: cắt gọn theo hình chữ nhật của vùng đó
        if (options.Area is { } area)
        {
            var corner1 = Map(new Pt(area.MinX, area.MinY), scale, offsetX, offsetY);
            var corner2 = Map(new Pt(area.MaxX, area.MaxY), scale, offsetX, offsetY);

            var x = Math.Min(corner1.X, corner2.X);
            var y = Math.Min(corner1.Y, corner2.Y);
            var w = Math.Abs(corner2.X - corner1.X);
            var h = Math.Abs(corner2.Y - corner1.Y);

            sb.Append($"{N(x)} {N(y)} {N(w)} {N(h)} re W n\n");
        }

        foreach (var entry in scene.Entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (visible is not null && entry.Entity is { } visibleEntity && !visible(visibleEntity)) continue;

            if (options.Area is { } clip && !entry.Bounds.IsEmpty && !entry.Bounds.Intersects(clip))
                continue;

            foreach (var prim in entry.Prims)
                AppendPrim(sb, prim, scale, offsetX, offsetY, usage, options, entry.Entity?.Layer?.Name);
        }

        sb.Append("Q\n");

        // 1 điểm = 1/72 inch → 1 mm = 72/25.4 point
        var unitsPerMm = scale > 1e-12 ? (72.0 / 25.4) / scale : 0;

        AppendTitleBlock(sb, options, pageW, pageH, margin, unitsPerMm, usage);

        return (sb.ToString(), new PdfExportResult(scale, unitsPerMm, bounds));
    }

    private static void AppendPrim(StringBuilder sb, Prim prim, double scale, double ox, double oy,
                                   FontUsage? usage, PdfExportOptions options, string? layerName = null)
    {
        var (color, widthPt) = ResolveStyle(prim, options, layerName);

        switch (prim.Kind)
        {
            case PrimKind.Text:
                AppendText(sb, prim, scale, ox, oy, usage, color);
                break;

            case PrimKind.Point:
            {
                var p = Map(prim.Position, scale, ox, oy);
                SetFill(sb, color);
                sb.Append($"{N(p.X - 1)} {N(p.Y - 1)} 2 2 re f\n");
                break;
            }

            case PrimKind.Hatch:
                AppendHatch(sb, prim, scale, ox, oy, color, widthPt);
                break;

            default:
                AppendPolyline(sb, prim, scale, ox, oy, color, widthPt);
                break;
        }
    }

    /// <summary>
    /// Độ đậm nét in (mm) theo bảng kiểu in nếu có, ngược lại theo quy tắc mặc định.
    /// Tách riêng để kiểm thử được mà không phải so sánh byte file PDF.
    /// </summary>
    public static double ResolveWidthMm(Prim prim, PdfExportOptions options, string? layerName)
    {
        if (options.PlotStyle is { } table)
        {
            var style = table.For(layerName);

            return style?.WeightMm ?? table.DefaultWeightMm;
        }

        return options.Monochrome
            ? Styles.LineWeights.FromAci(prim.AciIndex)
            : prim.LineWeightMm;
    }

    /// <summary>Màu và độ đậm nét khi in (có thể bị chế độ đơn sắc hoặc bảng kiểu in ghi đè).</summary>
    private static (Rgb Color, double WidthPt) ResolveStyle(Prim prim, PdfExportOptions options,
                                                            string? layerName = null)
    {
        // Bảng kiểu in theo lớp (thay .ctb) được ưu tiên nếu có
        if (options.PlotStyle is { } table)
        {
            var style = table.For(layerName);

            var styleColor = (style?.Black ?? table.Monochrome) ? new Rgb(0, 0, 0) : prim.Color;

            if (styleColor.R > 240 && styleColor.G > 240 && styleColor.B > 240)
                styleColor = new Rgb(0, 0, 0);

            var styleMm = style?.WeightMm ?? table.DefaultWeightMm;

            return (styleColor, Math.Clamp(styleMm * 72.0 / 25.4, 0.1, 12.0));
        }

        var color = options.Monochrome ? new Rgb(0, 0, 0) : prim.Color;

        // Trắng trên giấy trắng là vô hình — AutoCAD in ACI 7 thành đen
        if (color.R > 240 && color.G > 240 && color.B > 240)
            color = new Rgb(0, 0, 0);

        var mm = options.Monochrome
            ? Styles.LineWeights.FromAci(prim.AciIndex)
            : prim.LineWeightMm;

        var widthPt = Math.Clamp(mm * 72.0 / 25.4, 0.1, 12.0);
        return (color, widthPt);
    }

    private static void AppendPolyline(StringBuilder sb, Prim prim, double scale, double ox, double oy,
                                       Rgb color, double widthPt)
    {
        var points = prim.Points;
        if (points.Length < 2) return;

        sb.Append("q\n");

        if (prim.Dash is { Length: > 0 })
        {
            var pattern = string.Join(" ", prim.Dash.Select(d => N(Math.Max(0.1, Math.Abs(d) * scale))));
            sb.Append($"[{pattern}] 0 d\n");
        }

        SelectPen(sb, color, widthPt);

        var first = Map(points[0], scale, ox, oy);
        sb.Append($"{N(first.X)} {N(first.Y)} m\n");

        for (var i = 1; i < points.Length; i++)
        {
            var p = Map(points[i], scale, ox, oy);
            sb.Append($"{N(p.X)} {N(p.Y)} l\n");
        }

        if (prim.Closed) sb.Append("h\n");

        if (prim.Filled && prim.Closed)
        {
            SetFill(sb, color);
            sb.Append("B\n");     // vừa tô vừa viền
        }
        else
        {
            sb.Append("S\n");
        }

        sb.Append("Q\n");
    }

    private static void AppendHatch(StringBuilder sb, Prim prim, double scale, double ox, double oy,
                                    Rgb color, double widthPt)
    {
        if (prim.Loops is not { Length: > 0 }) return;

        sb.Append("q\n");

        // Dựng đường bao làm vùng cắt
        foreach (var loop in prim.Loops)
        {
            if (loop.Length < 3) continue;

            var first = Map(loop[0], scale, ox, oy);
            sb.Append($"{N(first.X)} {N(first.Y)} m\n");

            for (var i = 1; i < loop.Length; i++)
            {
                var p = Map(loop[i], scale, ox, oy);
                sb.Append($"{N(p.X)} {N(p.Y)} l\n");
            }
            sb.Append("h\n");
        }

        sb.Append("W n\n");    // clip

        SetFill(sb, color);

        if (prim.SolidFill)
        {
            // Tô lại toàn vùng bên trong clip, dùng even-odd để chừa lỗ
            foreach (var loop in prim.Loops)
            {
                if (loop.Length < 3) continue;
                var first = Map(loop[0], scale, ox, oy);
                sb.Append($"{N(first.X)} {N(first.Y)} m\n");
                for (var i = 1; i < loop.Length; i++)
                {
                    var p = Map(loop[i], scale, ox, oy);
                    sb.Append($"{N(p.X)} {N(p.Y)} l\n");
                }
                sb.Append("h\n");
            }
            sb.Append("f*\n");
        }
        else
        {
            // Pattern: gạch chéo trong vùng đã cắt
            var bounds = prim.Bounds();
            var center = bounds.Center;
            var diagonal = Math.Max(bounds.Width, bounds.Height) * scale * 1.6 + 20;
            var spacing = Math.Clamp(6.0 * prim.PatternSpacing, 4.0, 40.0);
            var c = Map(center, scale, ox, oy);

            var angle = prim.PatternAngle;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            var nx = -dy;
            var ny = dx;

            SelectPen(sb, color, Math.Max(0.1, widthPt * 0.6));

            for (var t = -diagonal; t <= diagonal; t += spacing)
            {
                var ax = c.X + nx * t - dx * diagonal;
                var ay = c.Y + ny * t - dy * diagonal;
                var bx = c.X + nx * t + dx * diagonal;
                var by = c.Y + ny * t + dy * diagonal;

                sb.Append($"{N(ax)} {N(ay)} m {N(bx)} {N(by)} l S\n");
            }
        }

        // Viền ngoài
        foreach (var loop in prim.Loops)
        {
            if (loop.Length < 3) continue;
            SelectPen(sb, color, widthPt);
            var first = Map(loop[0], scale, ox, oy);
            sb.Append($"{N(first.X)} {N(first.Y)} m\n");
            for (var i = 1; i < loop.Length; i++)
            {
                var p = Map(loop[i], scale, ox, oy);
                sb.Append($"{N(p.X)} {N(p.Y)} l\n");
            }
            sb.Append("h S\n");
        }

        sb.Append("Q\n");
    }

    private static void AppendText(StringBuilder sb, Prim prim, double scale, double ox, double oy,
                                   FontUsage? usage, Rgb color)
    {
        if (string.IsNullOrEmpty(prim.Text)) return;

        var size = prim.Height * scale;
        if (size < 2) return;

        var p = Map(prim.Position, scale, ox, oy);
        var cos = Math.Cos(prim.Rotation);
        var sin = Math.Sin(prim.Rotation);

        SetFill(sb, color);

        sb.Append("BT\n");
        sb.Append($"/{FontName} {N(size)} Tf\n");
        sb.Append($"{N(cos)} {N(sin)} {N(-sin)} {N(cos)} {N(p.X)} {N(p.Y)} Tm\n");

        if (usage is not null)
        {
            sb.Append($"{EncodeText(prim.Text, usage)} Tj\n");
        }
        else
        {
            var text = ToPdfString(prim.Text.Replace("\r", " ").Replace("\n", " "));
            sb.Append($"({text}) Tj\n");
        }

        sb.Append("ET\n");
    }

    /// <summary>Chuỗi văn bản PDF: dạng (literal) hoặc &lt;hex glyph id&gt; khi nhúng font.</summary>
    private static string EncodeText(string text, FontUsage? usage)
    {
        if (usage is null)
            return "(" + ToPdfString(text.Replace("\r", " ").Replace("\n", " ")) + ")";

        var hex = new StringBuilder(text.Length * 4);
        var fallback = usage.Font.GlyphId('?');

        foreach (var ch in text)
        {
            if (ch is '\r' or '\n') continue;

            var gid = usage.Font.GlyphId(ch);
            if (gid == 0) gid = fallback;

            usage.Glyphs.Add(gid);
            usage.ToUnicode[gid] = ch;
            hex.Append(gid.ToString("X4"));
        }

        return $"<{hex}>";
    }

    private static void AppendTitleBlock(StringBuilder sb, PdfExportOptions options,
                                         double pageW, double pageH, double margin,
                                         double unitsPerMm, FontUsage? usage)
    {
        var size = 7.0;
        var scaleLabel = unitsPerMm >= 1
            ? $"1:{N(unitsPerMm)}"
            : $"x{N(unitsPerMm)}";

        var line = $"OpenCAD  |  {options.Title}  |  ty le {scaleLabel}  |  {DateTime.Now:yyyy-MM-dd HH:mm}";

        SetFill(sb, new Rgb(110, 110, 110));
        sb.Append("BT\n");
        sb.Append($"/{FontName} {N(size)} Tf\n");
        sb.Append($"1 0 0 1 {N(margin)} {N(pageH - margin * 0.55)} Tm\n");
        sb.Append($"{EncodeText(line, usage)} Tj\n");
        sb.Append("ET\n");

        // Đường kẻ ngăn dải tiêu đề với vùng vẽ
        SelectPen(sb, new Rgb(200, 200, 200), 0.4);
        var separatorY = pageH - margin - PdfExportOptions.MmToPoints(9);
        sb.Append($"{N(margin)} {N(separatorY)} m {N(pageW - margin)} {N(separatorY)} l S\n");

        // Khung viền trang
        SelectPen(sb, new Rgb(190, 190, 190), 0.4);
        sb.Append($"{N(0.5)} {N(0.5)} {N(pageW - 1)} {N(pageH - 1)} re S\n");
    }

    // -------------------------------------------------------------- HELPERS

    private static Pt Map(Pt world, double scale, double ox, double oy)
        => new(world.X * scale + ox, world.Y * scale + oy);

    private static void SelectPen(StringBuilder sb, Rgb c, double width)
    {
        sb.Append($"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)} RG\n");
        sb.Append($"{N(width)} w\n");
    }

    private static void SetFill(StringBuilder sb, Rgb c)
        => sb.Append($"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)} rg\n");

    private static string N(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
        return Math.Round(value, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// PDF với font Type1 chuẩn chỉ mã hoá được WinAnsi, không có dấu tiếng Việt.
    /// Chuyển về chữ Latin cơ bản để PDF vẫn đọc được thay vì ra ký tự rác.
    /// </summary>
    private static string ToPdfString(string input)
    {
        var sb = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            var mapped = FoldVietnamese(ch);

            if (mapped is '\\') sb.Append("\\\\");
            else if (mapped is '(') sb.Append("\\(");
            else if (mapped is ')') sb.Append("\\)");
            else if (mapped >= 32 && mapped <= 126) sb.Append(mapped);
            else sb.Append('?');
        }

        return sb.ToString();
    }

    private static char FoldVietnamese(char c) => c switch
    {
        'à' or 'á' or 'ạ' or 'ả' or 'ã' or 'â' or 'ầ' or 'ấ' or 'ậ' or 'ẩ' or 'ẫ'
            or 'ă' or 'ằ' or 'ắ' or 'ặ' or 'ẳ' or 'ẵ' => 'a',
        'À' or 'Á' or 'Ạ' or 'Ả' or 'Ã' or 'Â' or 'Ầ' or 'Ấ' or 'Ậ' or 'Ẩ' or 'Ẫ'
            or 'Ă' or 'Ằ' or 'Ắ' or 'Ặ' or 'Ẳ' or 'Ẵ' => 'A',

        'è' or 'é' or 'ẹ' or 'ẻ' or 'ẽ' or 'ê' or 'ề' or 'ế' or 'ệ' or 'ể' or 'ễ' => 'e',
        'È' or 'É' or 'Ẹ' or 'Ẻ' or 'Ẽ' or 'Ê' or 'Ề' or 'Ế' or 'Ệ' or 'Ể' or 'Ễ' => 'E',

        'ì' or 'í' or 'ị' or 'ỉ' or 'ĩ' => 'i',
        'Ì' or 'Í' or 'Ị' or 'Ỉ' or 'Ĩ' => 'I',

        'ò' or 'ó' or 'ọ' or 'ỏ' or 'õ' or 'ô' or 'ồ' or 'ố' or 'ộ' or 'ổ' or 'ỗ'
            or 'ơ' or 'ờ' or 'ớ' or 'ợ' or 'ở' or 'ỡ' => 'o',
        'Ò' or 'Ó' or 'Ọ' or 'Ỏ' or 'Õ' or 'Ô' or 'Ồ' or 'Ố' or 'Ộ' or 'Ổ' or 'Ỗ'
            or 'Ơ' or 'Ờ' or 'Ớ' or 'Ợ' or 'Ở' or 'Ỡ' => 'O',

        'ù' or 'ú' or 'ụ' or 'ủ' or 'ũ' or 'ư' or 'ừ' or 'ứ' or 'ự' or 'ử' or 'ữ' => 'u',
        'Ù' or 'Ú' or 'Ụ' or 'Ủ' or 'Ũ' or 'Ư' or 'Ừ' or 'Ứ' or 'Ự' or 'Ử' or 'Ữ' => 'U',

        'ỳ' or 'ý' or 'ỵ' or 'ỷ' or 'ỹ' => 'y',
        'Ỳ' or 'Ý' or 'Ỵ' or 'Ỷ' or 'Ỹ' => 'Y',

        'đ' => 'd',
        'Đ' => 'D',

        // Dấu câu typographic → ASCII để không thành ký tự rác trong PDF
        '\u2014' or '\u2013' or '\u2012' => '-',
        '\u2018' or '\u2019' => '\'',
        '\u201C' or '\u201D' => '"',
        '\u2026' => '.',
        '\u00B0' => ' ',
        '\u00D7' => 'x',
        '\u00A0' => ' ',

        _ => c,
    };

    // ------------------------------------------------------------ PDF BYTES

    private static byte[] AssembleMultiPagePdf(
        IReadOnlyList<(string Content, PdfExportOptions Options)> pages,
        PdfExportOptions baseOptions, FontUsage? usage)
    {
        var pageCount = pages.Count;
        var embedded = usage is not null;

        var pagesId = 2;
        var fontId = 3 + 2 * pageCount;
        var infoId = fontId + 1;
        var descriptorId = fontId + 2;
        var fontFileId = fontId + 3;
        var cidFontId = fontId + 4;
        var toUnicodeId = fontId + 5;

        using var ms = new MemoryStream(256 * 1024);
        var offsets = new List<long>();

        void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        void BeginObject()
        {
            offsets.Add(ms.Position);
            Write($"{offsets.Count} 0 obj\n");
        }

        void EndObject() => Write("endobj\n");

        void ContentStream(string dictionary, byte[] data)
        {
            Write($"<< {dictionary} /Length {data.Length} >>\nstream\n");
            ms.Write(data, 0, data.Length);
            Write("\nendstream\n");
        }

        Write("%PDF-1.4\n");
        Write("%\u00E2\u00E3\u00CF\u00D3\n");

        // 1: Catalog
        BeginObject();
        Write($"<< /Type /Catalog /Pages {pagesId} 0 R >>\n");
        EndObject();

        // 2: Pages
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{3 + 2 * i} 0 R"));
        BeginObject();
        Write($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\n");
        EndObject();

        // Mỗi tờ: Page + Contents
        for (var i = 0; i < pageCount; i++)
        {
            var options = pages[i].Options;
            var pageObjId = 3 + 2 * i;
            var contentObjId = 4 + 2 * i;

            BeginObject();
            Write($"<< /Type /Page /Parent {pagesId} 0 R " +
                  $"/MediaBox [0 0 {N(options.PageWidthPoints)} {N(options.PageHeightPoints)}] " +
                  $"/Contents {contentObjId} 0 R " +
                  $"/Resources << /Font << /{FontName} {fontId} 0 R >> >> >>\n");
            EndObject();

            var contentBytes = Encoding.ASCII.GetBytes(pages[i].Content);
            BeginObject();
            ContentStream(string.Empty, contentBytes);
            EndObject();

            _ = pageObjId;
        }

        // Font
        BeginObject();
        if (embedded)
        {
            Write($"<< /Type /Font /Subtype /Type0 /BaseFont /{usage!.Font.PostScriptName} " +
                  $"/Encoding /Identity-H /DescendantFonts [{cidFontId} 0 R] /ToUnicode {toUnicodeId} 0 R >>\n");
        }
        else
        {
            Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\n");
        }
        EndObject();

        // Info
        BeginObject();
        Write($"<< /Title ({ToPdfString(baseOptions.Title)}) /Producer (OpenCAD) >>\n");
        EndObject();

        if (embedded)
        {
            var font = usage!.Font;

            BeginObject();
            Write($"<< /Type /FontDescriptor /FontName /{font.PostScriptName} /Flags 32 " +
                  $"/FontBBox [{font.XMin} {font.YMin} {font.XMax} {font.YMax}] /ItalicAngle 0 " +
                  $"/Ascent {font.Ascender} /Descent {font.Descender} /CapHeight {font.CapHeight} " +
                  $"/StemV 80 /FontFile2 {fontFileId} 0 R >>\n");
            EndObject();

            BeginObject();
            ContentStream($"/Length1 {font.Data.Length} /Filter /FlateDecode", Deflate(font.Data));
            EndObject();

            BeginObject();
            var widths = new StringBuilder();
            foreach (var gid in usage.Glyphs.OrderBy(g => g))
                widths.Append($"{gid} [{font.Width(gid)}] ");

            Write($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.PostScriptName} " +
                  "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                  $"/FontDescriptor {descriptorId} 0 R /DW 500 /W [{widths}] /CIDToGIDMap /Identity >>\n");
            EndObject();

            BeginObject();
            ContentStream("/Filter /FlateDecode", Deflate(Encoding.ASCII.GetBytes(BuildToUnicodeCMap(usage))));
            EndObject();
        }

        var xrefOffset = ms.Position;

        Write($"xref\n0 {offsets.Count + 1}\n");
        Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info {infoId} 0 R >>\n");
        Write($"startxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream(data.Length / 2 + 64);

        using (var zlib = new System.IO.Compression.ZLibStream(
                   output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static string BuildToUnicodeCMap(FontUsage usage)
    {
        var sb = new StringBuilder(4096);

        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        var pairs = usage.ToUnicode.OrderBy(p => p.Key).ToList();

        for (var i = 0; i < pairs.Count; i += 100)
        {
            var chunk = pairs.Skip(i).Take(100).ToList();
            sb.Append($"{chunk.Count} beginbfchar\n");

            foreach (var (gid, ch) in chunk)
                sb.Append($"<{gid:X4}> <{(int)ch:X4}>\n");

            sb.Append("endbfchar\n");
        }

        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return sb.ToString();
    }
}
