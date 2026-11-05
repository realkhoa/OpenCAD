using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Layout;

/// <summary>Một mảng hình trên tờ in, có thể bị cắt theo khung (dùng cho viewport).</summary>
public sealed class PaperItem
{
    public required IReadOnlyList<Prim> Prims { get; init; }
    public RectD? Clip { get; init; }
}

/// <summary>
/// Tờ in (paper space) với toạ độ tính bằng **mm** trên giấy.
/// Khác với model space ở chỗ đơn vị là mm giấy và mọi thứ đã đúng tỷ lệ in.
/// </summary>
public sealed class PaperSheet
{
    public string Name { get; set; } = "Layout1";
    public double WidthMm { get; set; } = 420;
    public double HeightMm { get; set; } = 297;
    public List<PaperItem> Items { get; } = new();

    /// <summary>Vùng dành cho hình vẽ (trừ khung tên) — dùng khi tạo viewport thật.</summary>
    public RectD? DrawingArea { get; set; }

    /// <summary>Thông số viewport nếu tờ có hình vẽ — để ghi thành Viewport entity trong DWG.</summary>
    public PaperViewport? Viewport { get; set; }

    public RectD Bounds => new(0, 0, WidthMm, HeightMm);

    public void Add(IReadOnlyList<Prim> prims, RectD? clip = null)
    {
        if (prims.Count == 0) return;
        Items.Add(new PaperItem { Prims = prims, Clip = clip });
    }
}

/// <summary>Thông số một viewport trên tờ in.</summary>
public sealed class PaperViewport
{
    public RectD Rect { get; set; }
    public Pt ViewCenter { get; set; }
    public double ViewHeight { get; set; }
    public double UnitsPerMm { get; set; }
}

public sealed class SheetTemplateOptions
{
    public string Title { get; set; } = "Bản vẽ";
    public string? Project { get; set; }
    public string? Drafter { get; set; }
    public double WidthMm { get; set; } = 420;     // A3 ngang
    public double HeightMm { get; set; } = 297;
    public double MarginMm { get; set; } = 10;

    /// <summary>Tỷ lệ in: số đơn vị bản vẽ trên 1 mm giấy (1:50 → 50).</summary>
    public double UnitsPerMm { get; set; } = 50;

    public double TitleBlockWidthMm { get; set; } = 180;
    public double TitleBlockHeightMm { get; set; } = 40;
}

/// <summary>
/// Dựng một tờ in hoàn chỉnh từ bản vẽ model space: khung viền, khung tên,
/// và một viewport đặt bản vẽ đúng tỷ lệ.
/// </summary>
public static class PaperSheetBuilder
{
    private static readonly Rgb Ink = new(20, 20, 20);
    private static readonly Rgb Frame = new(60, 60, 60);

    public static PaperSheet Build(CadScene scene, SheetTemplateOptions options,
                                   bool includeViewportContent = true)
    {
        var sheet = new PaperSheet
        {
            Name = "Layout1",
            WidthMm = options.WidthMm,
            HeightMm = options.HeightMm,
        };

        var margin = options.MarginMm;
        var inner = new RectD(margin, margin, options.WidthMm - margin, options.HeightMm - margin);

        // Khung viền ngoài + khung trong
        sheet.Add(RectangleLines(new RectD(margin * 0.5, margin * 0.5,
                                          options.WidthMm - margin * 0.5, options.HeightMm - margin * 0.5), Frame));
        sheet.Add(RectangleLines(inner, Frame));

        // Khung tên ở góc dưới phải
        var tbWidth = Math.Min(options.TitleBlockWidthMm, inner.Width);
        var tbHeight = Math.Min(options.TitleBlockHeightMm, inner.Height);

        var titleBlock = new RectD(inner.MaxX - tbWidth, inner.MinY, inner.MaxX, inner.MinY + tbHeight);
        sheet.Add(RectangleLines(titleBlock, Frame));

        sheet.Add(TitleBlockContent(titleBlock, options));

        // Vùng dành cho hình vẽ: phần còn lại phía trên khung tên
        var drawingArea = new RectD(inner.MinX, titleBlock.MaxY, inner.MaxX, inner.MaxY);
        sheet.DrawingArea = drawingArea;

        var viewport = FitViewport(scene, drawingArea, options.UnitsPerMm);
        if (viewport is not null)
        {
            var (prims, clip, actualUnitsPerMm, viewCenter, viewHeight) = viewport.Value;

            if (includeViewportContent) sheet.Add(prims, clip);
            else sheet.Add(prims, clip);   // hình học đã biến đổi, vẫn cần cho bản in

            sheet.Viewport = new PaperViewport
            {
                Rect = clip,
                ViewCenter = viewCenter,
                ViewHeight = viewHeight,
                UnitsPerMm = actualUnitsPerMm,
            };

            // Ghi tỷ lệ thật vào khung tên (có thể lệch nếu bản vẽ quá lớn)
            options.UnitsPerMm = actualUnitsPerMm;
        }

        // Khung viewport
        if (viewport is not null)
            sheet.Add(RectangleLines(sheet.Viewport!.Rect, Frame));

        return sheet;
    }

    private static (List<Prim> Prims, RectD Clip, double UnitsPerMm, Pt ViewCenter, double ViewHeight)? FitViewport(
        CadScene scene, RectD area, double unitsPerMm)
    {
        if (area.Width <= 1 || area.Height <= 1) return null;

        var bounds = scene.Bounds;
        if (bounds.IsEmpty || (bounds.Width < 1e-9 && bounds.Height < 1e-9)) return null;

        // Nếu bản vẽ không vừa ở tỷ lệ yêu cầu thì tự nới tỷ lệ cho vừa
        var effective = unitsPerMm;
        var widthMm = bounds.Width / effective;
        var heightMm = bounds.Height / effective;

        var shrink = Math.Max(widthMm / area.Width, heightMm / area.Height);
        if (shrink > 1) effective *= shrink;

        // Làm tròn lên các tỷ lệ quen dùng
        effective = RoundToNiceScale(effective);

        var scale = 1.0 / effective;              // mm giấy trên 1 đơn vị bản vẽ

        var drawW = bounds.Width * scale;
        var drawH = bounds.Height * scale;

        var offsetX = area.MinX + (area.Width - drawW) / 2 - bounds.MinX * scale;
        var offsetY = area.MinY + (area.Height - drawH) / 2 - bounds.MinY * scale;

        var xf = new Affine2(scale, 0, 0, scale, offsetX, offsetY);

        var prims = new List<Prim>(scene.Entries.Count * 2);
        foreach (var entry in scene.Entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (entry.Entity.Layer is { IsOn: false }) continue;

            foreach (var prim in entry.Prims)
                prims.Add(TransformPrim(prim, xf));
        }

        return (prims, area, effective, bounds.Center, area.Height * effective);
    }

    private static double RoundToNiceScale(double value)
    {
        double[] nice = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000 };
        foreach (var candidate in nice)
            if (value <= candidate) return candidate;

        return Math.Ceiling(value / 1000) * 1000;
    }

    private static Prim TransformPrim(Prim prim, Affine2 xf)
    {
        switch (prim.Kind)
        {
            case PrimKind.Text:
            {
                var transformed = Prim.TextAt(
                    prim.Text,
                    xf.Apply(prim.Position),
                    prim.Height * xf.ScaleFactor,
                    prim.Rotation + xf.RotationAngle,
                    prim.Color);

                transformed.LineWeightMm = prim.LineWeightMm;
                transformed.AciIndex = prim.AciIndex;
                transformed.Dash = prim.Dash;
                return transformed;
            }

            case PrimKind.Point:
                return Prim.PointAt(xf.Apply(prim.Position), prim.Color);

            case PrimKind.Hatch:
            {
                var loops = prim.Loops?.Select(l => l.Select(xf.Apply).ToArray()).ToArray();
                var hatch = Prim.HatchRegion(loops ?? Array.Empty<Pt[]>(), prim.Color,
                    prim.SolidFill, prim.PatternAngle, prim.PatternSpacing);
                hatch.LineWeightMm = prim.LineWeightMm;
                hatch.AciIndex = prim.AciIndex;
                return hatch;
            }

            default:
            {
                var mapped = new Pt[prim.Points.Length];
                for (var i = 0; i < mapped.Length; i++) mapped[i] = xf.Apply(prim.Points[i]);

                var polyline = Prim.Polyline(mapped, prim.Color, prim.Closed, prim.Filled);
                polyline.LineWeightMm = prim.LineWeightMm;
                polyline.AciIndex = prim.AciIndex;
                polyline.Dash = prim.Dash;
                return polyline;
            }
        }
    }

    private static List<Prim> RectangleLines(RectD rect, Rgb color)
    {
        var points = new[]
        {
            new Pt(rect.MinX, rect.MinY),
            new Pt(rect.MaxX, rect.MinY),
            new Pt(rect.MaxX, rect.MaxY),
            new Pt(rect.MinX, rect.MaxY),
        };

        var prim = Prim.Polyline(points, color, closed: true);
        prim.LineWeightMm = 0.35;
        return new List<Prim> { prim };
    }

    private static List<Prim> TitleBlockContent(RectD block, SheetTemplateOptions options)
    {
        var prims = new List<Prim>();
        var rowHeight = block.Height / 4;
        var pad = 3.0;

        // Các dòng kẻ ngang
        for (var i = 1; i < 4; i++)
        {
            var y = block.MinY + rowHeight * i;
            var line = Prim.Polyline(new[] { new Pt(block.MinX, y), new Pt(block.MaxX, y) }, Frame);
            line.LineWeightMm = 0.2;
            prims.Add(line);
        }

        var textHeight = Math.Min(3.5, rowHeight * 0.4);
        var smallHeight = Math.Min(2.5, rowHeight * 0.28);

        void AddText(string text, double x, double y, double height, bool bold = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var prim = Prim.TextAt(text, new Pt(x, y), height, 0, Ink);
            prim.LineWeightMm = bold ? 0.35 : 0.25;
            prims.Add(prim);
        }

        // Dòng 1: tên bản vẽ (chữ lớn)
        AddText(options.Title, block.MinX + pad, block.MinY + rowHeight * 2.6, textHeight, bold: true);

        // Dòng 2: dự án
        AddText(options.Project ?? "", block.MinX + pad, block.MinY + rowHeight * 1.6, smallHeight);

        // Dòng 3: tỷ lệ + ngày
        AddText($"Tỷ lệ 1:{options.UnitsPerMm:0.##}", block.MinX + pad, block.MinY + rowHeight * 0.6, smallHeight);

        AddText(DateTime.Now.ToString("yyyy-MM-dd"),
            block.MinX + block.Width * 0.55, block.MinY + rowHeight * 0.6, smallHeight);

        // Dòng 4: người vẽ
        AddText(options.Drafter ?? "OpenCAD",
            block.MinX + block.Width * 0.55, block.MinY + rowHeight * 1.6, smallHeight);

        return prims;
    }
}
