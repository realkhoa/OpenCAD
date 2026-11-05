using System.Drawing.Drawing2D;

namespace OpenCad.App;

/// <summary>
/// @brief Enumeration of vector glyph types representing AutoCAD CAD tools and interface elements.
/// </summary>
internal enum Glyph
{
    Select, Line, Polyline, Rectangle, Circle, Arc, Ellipse, Polygon, Text, Hatch,
    Dimension, Insert, Block,
    Move, Copy, Rotate, Scale, Mirror, Offset, Trim, Extend, Fillet, Chamfer, Erase,
    Undo, Redo, ZoomExtents, Ortho, Osnap, Lwt, Grid,
    AppMenu, Layers, Properties, Measure, Pan, ZoomWindow, Orbit, Model, Paper, Coordinates,
}

/// <summary>
/// @brief Vector icon generation engine rendering authentic AutoCAD-styled icons via GDI+ vector paths.
/// @details Generates resolution-independent bitmap icons cached by glyph and dimension, avoiding
/// reliance on external image files and enabling crisp rendering across various DPI settings.
/// </summary>
internal static class Icons
{
    private static readonly Dictionary<(Glyph Glyph, int Size), Bitmap> Cache = new();

    private static readonly Color Ink = Color.FromArgb(228, 232, 236);
    private static readonly Color Accent = Color.FromArgb(0, 174, 239);
    private static readonly Color Faint = Color.FromArgb(120, 128, 136);

    /// <summary>
    /// @brief Retrieves a cached bitmap icon of standard 24x24 dimension for the specified glyph.
    /// @param glyph The glyph identifier to retrieve.
    /// @return A cached Bitmap representation of the glyph.
    /// </summary>
    public static Bitmap Get(Glyph glyph) => Get(glyph, 24);

    /// <summary>
    /// @brief Retrieves a cached bitmap icon with custom square dimensions for the specified glyph.
    /// @param glyph The glyph identifier to retrieve.
    /// @param size The width and height in pixels of the returned icon.
    /// @return A cached Bitmap representation of the glyph rendered at the requested dimension.
    /// </summary>
    public static Bitmap Get(Glyph glyph, int size)
    {
        var key = (glyph, size);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bitmap = Create(glyph, size);
        Cache[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// @brief Creates a new vector bitmap icon for the given glyph at the specified size.
    /// @param glyph The vector glyph type to render.
    /// @param size The dimensions in pixels.
    /// @return Rendered 32bpp ARGB Bitmap.
    /// </summary>
    private static Bitmap Create(Glyph glyph, int size)
    {
        var bmp = new Bitmap(size, size);
        bmp.SetResolution(96, 96);

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var scale = size / 24f;
        g.ScaleTransform(scale, scale);

        using var pen = new Pen(Ink, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var accentPen = new Pen(Accent, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var faintPen = new Pen(Faint, 1.2f) { DashStyle = DashStyle.Dash };
        using var brush = new SolidBrush(Accent);
        using var accentBrush = new SolidBrush(Accent);
        using var inkBrush = new SolidBrush(Ink);
        using var faintBrush = new SolidBrush(Faint);
        using var font = new Font("Segoe UI", 11f, FontStyle.Bold);

        switch (glyph)
        {
            case Glyph.Select:
                g.FillPolygon(inkBrush, new[] { new PointF(7, 4), new PointF(7, 18), new PointF(11, 14), new PointF(14, 20), new PointF(16.5f, 18.5f), new PointF(13.5f, 13), new PointF(18, 12) });
                break;
            case Glyph.Line:
                g.DrawLine(pen, 5, 19, 19, 5);
                break;
            case Glyph.Polyline:
                g.DrawLines(pen, new[] { new PointF(3, 18), new PointF(8, 8), new PointF(13, 16), new PointF(21, 5) });
                break;
            case Glyph.Rectangle:
                g.DrawRectangle(pen, 4, 6, 16, 12);
                break;
            case Glyph.Circle:
                g.DrawEllipse(pen, 4, 4, 16, 16);
                break;
            case Glyph.Arc:
                g.DrawArc(pen, 4, 6, 16, 16, 160, 220);
                g.FillEllipse(accentBrush, 4.5f, 12.5f, 3, 3);
                break;
            case Glyph.Ellipse:
                g.DrawEllipse(pen, 3, 7, 18, 10);
                break;
            case Glyph.Polygon:
                var polyPoints = new PointF[6];
                for (var i = 0; i < 6; i++)
                {
                    var angle = Math.PI / 3 * i - Math.PI / 2;
                    polyPoints[i] = new PointF((float)(12 + 8 * Math.Cos(angle)), (float)(12 + 8 * Math.Sin(angle)));
                }
                g.DrawPolygon(pen, polyPoints);
                break;
            case Glyph.Text:
                g.DrawString("A", font, inkBrush, 5, 2);
                break;
            case Glyph.Hatch:
                g.DrawRectangle(pen, 4, 5, 16, 14);
                for (var x = 2; x < 24; x += 5) g.DrawLine(faintPen, x, 19, x + 8, 5);
                break;
            case Glyph.Dimension:
                g.DrawLine(pen, 4, 7, 4, 19);
                g.DrawLine(pen, 20, 7, 20, 19);
                g.DrawLine(accentPen, 4, 13, 20, 13);
                g.FillPolygon(inkBrush, new[] { new PointF(4, 13), new PointF(8, 10.5f), new PointF(8, 15.5f) });
                g.FillPolygon(inkBrush, new[] { new PointF(20, 13), new PointF(16, 10.5f), new PointF(16, 15.5f) });
                break;
            case Glyph.Insert:
                g.DrawRectangle(pen, 3, 3, 11, 11);
                g.DrawLine(accentPen, 17, 5, 17, 15);
                g.DrawLine(accentPen, 12, 10, 22, 10);
                break;
            case Glyph.Block:
                g.DrawRectangle(pen, 3, 8, 10, 10);
                g.DrawRectangle(faintPen, 9, 4, 10, 10);
                break;
            case Glyph.Move:
                g.DrawLine(accentPen, 4, 12, 20, 12);
                g.DrawLine(accentPen, 12, 4, 12, 20);
                g.FillPolygon(inkBrush, new[] { new PointF(20, 12), new PointF(16, 9), new PointF(16, 15) });
                g.FillPolygon(inkBrush, new[] { new PointF(4, 12), new PointF(8, 9), new PointF(8, 15) });
                break;
            case Glyph.Copy:
                g.DrawRectangle(pen, 3, 3, 12, 12);
                g.DrawRectangle(accentPen, 9, 9, 12, 12);
                break;
            case Glyph.Rotate:
                g.DrawArc(pen, 4, 4, 16, 16, 40, 250);
                g.FillPolygon(inkBrush, new[] { new PointF(18, 4), new PointF(20, 9), new PointF(15, 8) });
                g.FillEllipse(accentBrush, 10.5f, 10.5f, 3, 3);
                break;
            case Glyph.Scale:
                g.DrawRectangle(faintPen, 4, 10, 10, 10);
                g.DrawRectangle(accentPen, 4, 4, 16, 16);
                g.DrawLine(pen, 4, 20, 20, 4);
                break;
            case Glyph.Mirror:
                g.DrawLine(faintPen, 12, 3, 12, 21);
                g.FillPolygon(inkBrush, new[] { new PointF(3, 18), new PointF(3, 7), new PointF(9, 12) });
                g.DrawPolygon(accentPen, new[] { new PointF(21, 18), new PointF(21, 7), new PointF(15, 12) });
                break;
            case Glyph.Offset:
                g.DrawLines(pen, new[] { new PointF(4, 17), new PointF(11, 5), new PointF(20, 17) });
                g.DrawLines(accentPen, new[] { new PointF(4, 21), new PointF(11, 10), new PointF(20, 21) });
                break;
            case Glyph.Trim:
                g.DrawLine(faintPen, 3, 12, 21, 12);
                g.DrawLine(pen, 5, 5, 19, 19);
                g.DrawEllipse(accentPen, 2, 16, 5, 5);
                g.DrawEllipse(accentPen, 17, 16, 5, 5);
                break;
            case Glyph.Extend:
                g.DrawLine(pen, 3, 12, 14, 12);
                g.DrawLine(accentPen, 19, 4, 19, 20);
                g.FillPolygon(inkBrush, new[] { new PointF(15, 12), new PointF(10, 9), new PointF(10, 15) });
                break;
            case Glyph.Fillet:
                g.DrawLine(pen, 5, 20, 5, 11);
                g.DrawArc(accentPen, 5, 5, 14, 14, 90, 90);
                g.DrawLine(pen, 12, 5, 20, 5);
                break;
            case Glyph.Chamfer:
                g.DrawLine(pen, 5, 20, 5, 11);
                g.DrawLine(accentPen, 5, 11, 11, 5);
                g.DrawLine(pen, 11, 5, 20, 5);
                break;
            case Glyph.Erase:
                g.DrawLine(pen, 5, 5, 19, 19);
                g.DrawLine(pen, 19, 5, 5, 19);
                break;
            case Glyph.Undo:
                g.DrawArc(pen, 4, 6, 16, 14, 200, 250);
                g.FillPolygon(inkBrush, new[] { new PointF(4, 5), new PointF(10, 7), new PointF(5, 11) });
                break;
            case Glyph.Redo:
                g.DrawArc(pen, 4, 6, 16, 14, 90, 250);
                g.FillPolygon(inkBrush, new[] { new PointF(20, 5), new PointF(14, 7), new PointF(19, 11) });
                break;
            case Glyph.ZoomExtents:
                g.DrawEllipse(pen, 3, 3, 13, 13);
                g.DrawLine(pen, 14, 14, 21, 21);
                g.DrawLine(accentPen, 7, 6, 12, 6);
                break;
            case Glyph.Ortho:
                g.DrawLine(accentPen, 5, 19, 19, 19);
                g.DrawLine(accentPen, 5, 19, 5, 5);
                g.FillRectangle(inkBrush, 3.5f, 17.5f, 3, 3);
                break;
            case Glyph.Osnap:
                g.DrawLine(faintPen, 4, 8, 20, 8);
                g.DrawLine(faintPen, 8, 4, 8, 20);
                g.DrawRectangle(accentPen, 5, 5, 6, 6);
                break;
            case Glyph.Lwt:
                g.DrawLine(new Pen(Ink, 1f), 4, 7, 20, 7);
                g.DrawLine(new Pen(Ink, 2.4f), 4, 12, 20, 12);
                g.DrawLine(new Pen(Ink, 4f), 4, 17, 20, 17);
                break;
            case Glyph.Grid:
                for (var x = 0; x < 3; x++)
                    for (var y = 0; y < 3; y++)
                        g.FillRectangle(faintBrush, 5 + x * 6, 5 + y * 6, 2, 2);
                break;
            case Glyph.AppMenu:
                using (var redBrush = new SolidBrush(Color.FromArgb(200, 16, 46)))
                    g.FillRoundedRectangle(redBrush, new RectangleF(2, 2, 20, 20), 3f);
                using (var aFont = new Font("Arial", 12f, FontStyle.Bold))
                    g.DrawString("A", aFont, Brushes.White, 4.5f, 2.5f);
                break;
            case Glyph.Layers:
                g.DrawPolygon(pen, new[] { new PointF(12, 4), new PointF(20, 8), new PointF(12, 12), new PointF(4, 8) });
                g.DrawLines(accentPen, new[] { new PointF(4, 12), new PointF(12, 16), new PointF(20, 12) });
                g.DrawLines(pen, new[] { new PointF(4, 16), new PointF(12, 20), new PointF(20, 16) });
                break;
            case Glyph.Properties:
                g.DrawRectangle(pen, 5, 3, 14, 18);
                g.DrawLine(accentPen, 8, 7, 16, 7);
                g.DrawLine(accentPen, 8, 11, 16, 11);
                g.DrawLine(faintPen, 8, 15, 14, 15);
                break;
            case Glyph.Measure:
                g.DrawRectangle(pen, 3, 8, 18, 8);
                for (var x = 6; x < 20; x += 3) g.DrawLine(accentPen, x, 8, x, 12);
                break;
            case Glyph.Pan:
                g.DrawLines(pen, new[] { new PointF(7, 14), new PointF(7, 7), new PointF(9, 7), new PointF(9, 13), new PointF(11, 5), new PointF(13, 5), new PointF(13, 13), new PointF(15, 7), new PointF(17, 7), new PointF(17, 16), new PointF(15, 20), new PointF(8, 20), new PointF(5, 16), new PointF(7, 14) });
                break;
            case Glyph.ZoomWindow:
                g.DrawRectangle(faintPen, 4, 4, 16, 16);
                g.DrawEllipse(accentPen, 8, 8, 8, 8);
                break;
            case Glyph.Orbit:
                g.DrawEllipse(faintPen, 4, 4, 16, 16);
                g.DrawLine(accentPen, 12, 3, 12, 21);
                g.DrawLine(accentPen, 3, 12, 21, 12);
                g.FillEllipse(inkBrush, 10.5f, 10.5f, 3, 3);
                break;
            case Glyph.Model:
                g.DrawRectangle(pen, 4, 4, 16, 16);
                g.DrawLine(faintPen, 4, 12, 20, 12);
                g.DrawLine(faintPen, 12, 4, 12, 20);
                break;
            case Glyph.Paper:
                g.DrawRectangle(pen, 5, 3, 14, 18);
                g.DrawRectangle(accentPen, 8, 6, 8, 10);
                break;
            case Glyph.Coordinates:
                g.DrawLine(accentPen, 4, 18, 18, 18);
                g.DrawLine(accentPen, 4, 18, 4, 4);
                g.FillPolygon(inkBrush, new[] { new PointF(18, 18), new PointF(14, 16), new PointF(14, 20) });
                g.FillPolygon(inkBrush, new[] { new PointF(4, 4), new PointF(2, 8), new PointF(6, 8) });
                break;
        }

        return bmp;
    }

    /// <summary>
    /// @brief Internal helper to draw a filled rounded rectangle.
    /// @param g The graphics device context.
    /// @param brush The solid or gradient brush.
    /// @param bounds The target bounding rectangle.
    /// @param radius Corner radius in pixels.
    /// </summary>
    private static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(bounds.Right - 2 * radius, bounds.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(bounds.Right - 2 * radius, bounds.Bottom - 2 * radius, radius * 2, radius * 2, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - 2 * radius, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
