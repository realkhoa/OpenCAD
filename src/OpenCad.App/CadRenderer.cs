using System.Drawing.Drawing2D;
using ACadSharp.Entities;
using OpenCad.Core;
using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.App;

/// <summary>Vẽ <see cref="CadScene"/> bằng GDI+.</summary>
public sealed class CadRenderer : IDisposable
{
    private readonly Dictionary<Rgb, Pen> _penCache = new();
    private readonly Dictionary<int, Font> _fontCache = new();
    private FontFamily _fontFamily;
    private readonly SolidBrush _brush = new(Color.White);

    public Color Background { get; set; } = AutoCadTheme.CanvasBackground;
    public Color SelectionColor { get; set; } = Color.FromArgb(0, 200, 255);

    /// <summary>Hiển thị độ đậm nét (LWT) thay vì mọi nét cùng 1px.</summary>
    public bool ShowLineWeights { get; set; }

    /// <summary>Đang vẽ trên nền giấy trắng: nét trắng phải đổi thành đen.</summary>
    public bool PaperMode { get; set; }

    public CadRenderer()
    {
        _fontFamily = new FontFamily(DefaultFontName);
    }

    /// <summary>Font mặc định khi bản vẽ không chỉ định.</summary>
    public const string DefaultFontName = "Segoe UI";

    /// <summary>
    /// Đổi font chữ hiển thị. Tên không hợp lệ thì giữ font cũ. Trả về true nếu đổi được.
    /// </summary>
    public bool SetFont(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            var family = new FontFamily(name);

            foreach (var f in _fontCache.Values) f.Dispose();
            _fontCache.Clear();

            _fontFamily = family;

            return true;
        }
        catch { return false; }
    }

    /// <summary>Tên font đang dùng.</summary>
    public string FontName => _fontFamily.Name;

    public void Draw(Graphics g, CadScene? scene, ViewTransform view,
                     ISet<Entity>? selection, Func<Entity, bool>? visible = null)
    {        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Background);

        if (scene is null) return;

        var viewBounds = WorldViewBounds(view).Inflate(view.PixelsToWorld(4));

        foreach (var entry in scene.Entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (visible is not null && !visible(entry.Entity)) continue;

            // Culling: bỏ qua entity nằm ngoài khung nhìn
            if (!entry.Bounds.IsEmpty &&
                !entry.Bounds.Inflate(view.PixelsToWorld(8)).Intersects(viewBounds))
                continue;

            var selected = selection is not null && selection.Contains(entry.Entity);

            foreach (var prim in entry.Prims)
                DrawPrim(g, prim, view, selected);
        }
    }

    /// <summary>Vẽ một danh sách primitive (dùng cho chế độ paper space).</summary>
    public void DrawPrims(Graphics g, IReadOnlyList<Prim> prims, ViewTransform view, bool selected = false)
    {
        foreach (var prim in prims)
            DrawPrim(g, prim, view, selected);
    }

    private static RectD WorldViewBounds(ViewTransform view)
    {
        var topLeft = view.ToWorld(new PointF(0, 0));
        var bottomRight = view.ToWorld(new PointF(view.Width, view.Height));
        return new RectD(
            Math.Min(topLeft.X, bottomRight.X), Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(topLeft.X, bottomRight.X), Math.Max(topLeft.Y, bottomRight.Y));
    }

    private void DrawPrim(Graphics g, Prim prim, ViewTransform view, bool selected)
    {
        var color = selected ? SelectionColor : ToColor(prim.Color);

        // Trên nền giấy trắng, nét trắng là vô hình — AutoCAD vẽ ACI 7 thành đen
        if (!selected && PaperMode && color.R > 240 && color.G > 240 && color.B > 240)
            color = Color.FromArgb(20, 20, 20);
        var width = selected
            ? 2f
            : ShowLineWeights
                ? (float)Math.Clamp(prim.LineWeightMm * 3.5, 0.75, 8.0)
                : 1f;

        switch (prim.Kind)
        {
            case PrimKind.Point:
            {
                var p = view.ToScreen(prim.Position);
                using var b = new SolidBrush(color);
                g.FillRectangle(b, p.X - 2f, p.Y - 2f, 4f, 4f);
                break;
            }

            case PrimKind.Text:
            {
                var h = prim.Height * view.Zoom;
                if (h < 3) break;
                if (prim.Text.Length == 0) break;

                var font = GetFont((int)Math.Clamp(h, 3, 400));
                var p = view.ToScreen(prim.Position);

                var state = g.Save();
                if (Math.Abs(prim.Rotation) > 1e-6)
                {
                    g.TranslateTransform(p.X, p.Y);
                    g.RotateTransform((float)(-prim.Rotation * 180.0 / Math.PI));
                    p = PointF.Empty;
                }

                _brush.Color = color;
                var lines = prim.Text.Replace("\r", string.Empty).Split('\n');
                var lineStep = (float)(h * 1.2);
                for (var i = 0; i < lines.Length; i++)
                    g.DrawString(lines[i], font, _brush, p.X, p.Y + i * lineStep);

                g.Restore(state);
                break;
            }

            case PrimKind.Hatch:
                DrawHatch(g, prim, view, selected, color, width);
                break;

            default:
            {
                var pts = prim.Points;
                if (pts.Length < 2) break;

                var screen = new PointF[pts.Length];
                var anyVisible = false;
                for (var i = 0; i < pts.Length; i++)
                {
                    screen[i] = view.ToScreen(pts[i]);
                    if (!anyVisible &&
                        screen[i].X > -10_000 && screen[i].X < view.Width + 10_000 &&
                        screen[i].Y > -10_000 && screen[i].Y < view.Height + 10_000)
                        anyVisible = true;
                }
                if (!anyVisible) break;

                if (prim.Filled && prim.Closed)
                {
                    using var b = new SolidBrush(color);
                    g.FillPolygon(b, screen);
                }

                var pen = GetPen(color, width);

                if (prim.Dash is { Length: > 0 } && !prim.Filled)
                {
                    DrawDashedPath(g, screen, prim.Closed, prim.Dash, view.Zoom, pen);
                    break;
                }

                if (prim.Closed)
                {
                    var closed = new PointF[screen.Length + 1];
                    Array.Copy(screen, closed, screen.Length);
                    closed[^1] = screen[0];
                    screen = closed;
                }

                g.DrawLines(pen, screen);
                break;
            }
        }
    }

    /// <summary>
    /// Vẽ đa tuyến theo mẫu nét đứt. Mẫu được quy đổi sang pixel và có sàn tối thiểu
    /// để nét không biến mất khi zoom xa (bản vẽ mm thường có pattern rất nhỏ).
    /// </summary>
    private void DrawHatch(Graphics g, Prim prim, ViewTransform view, bool selected,
                           Color color, float width)
    {
        if (prim.Loops is not { Length: > 0 }) return;

        using var path = new GraphicsPath(FillMode.Alternate);

        foreach (var loop in prim.Loops)
        {
            if (loop.Length < 3) continue;

            var screen = new PointF[loop.Length];
            for (var i = 0; i < loop.Length; i++) screen[i] = view.ToScreen(loop[i]);
            path.AddPolygon(screen);
        }

        if (prim.SolidFill)
        {
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);

            if (selected)
            {
                using var selPen = new Pen(SelectionColor, 1.6f);
                g.DrawPath(selPen, path);
            }
            return;
        }

        // Hatch có pattern: gạch chéo, cắt gọn trong vùng
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 && bounds.Height <= 0) return;

        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        var diagonal = Math.Max(bounds.Width, bounds.Height) * 1.5f;
        var spacing = (float)Math.Clamp(6.0 * prim.PatternSpacing, 4.0, 40.0);

        var state = g.Save();
        g.SetClip(path);

        using (var lineBrush = new SolidBrush(Color.FromArgb(170, color)))
        using (var linePen = new Pen(lineBrush, 1f))
        {
            g.TranslateTransform(cx, cy);
            g.RotateTransform((float)(prim.PatternAngle * 180.0 / Math.PI));

            for (var y = -diagonal; y <= diagonal; y += spacing)
                g.DrawLine(linePen, -diagonal, y, diagonal, y);
        }

        g.Restore(state);

        using var outline = new Pen(color, selected ? 1.8f : 0.8f);
        g.DrawPath(outline, path);
    }

    private static void DrawDashedPath(Graphics g, PointF[] points, bool closed,
                                       double[] pattern, double zoom, Pen pen)
    {
        if (pattern.Length == 0) return;

        // Quy đổi sang pixel, đảm bảo mỗi đoạn tối thiểu 2px
        var px = new float[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
            px[i] = (float)Math.Max(2.0, Math.Abs(pattern[i]) * zoom);

        var index = 0;
        var phase = 0f;
        var drawing = pattern[0] > 0;

        var count = closed ? points.Length : points.Length - 1;

        for (var s = 0; s < count; s++)
        {
            var a = points[s];
            var b = points[(s + 1) % points.Length];

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var segLen = (float)Math.Sqrt(dx * dx + dy * dy);
            if (segLen < 1e-4f) continue;

            var ux = dx / segLen;
            var uy = dy / segLen;
            var t = 0f;

            while (t < segLen)
            {
                var remaining = px[index] - phase;
                var step = Math.Min(remaining, segLen - t);

                if (drawing && step > 0)
                {
                    g.DrawLine(pen,
                        a.X + ux * t, a.Y + uy * t,
                        a.X + ux * (t + step), a.Y + uy * (t + step));
                }

                t += step;
                phase += step;

                if (phase >= px[index] - 1e-3f)
                {
                    phase = 0;
                    index = (index + 1) % px.Length;
                    drawing = pattern[index] > 0;
                }
            }
        }
    }

    private Pen GetPen(Color color, float width)
    {
        var key = new Rgb(color.R, color.G, color.B);
        if (_penCache.TryGetValue(key, out var pen) && Math.Abs(pen.Width - width) < 0.01f)
            return pen;

        var p = new Pen(color, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        _penCache[key] = p;
        return p;
    }

    private Font GetFont(int pixelSize)
    {
        if (_fontCache.TryGetValue(pixelSize, out var f)) return f;
        f = new Font(_fontFamily, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        _fontCache[pixelSize] = f;
        return f;
    }

    private static Color ToColor(Rgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    public void Dispose()
    {
        foreach (var p in _penCache.Values) p.Dispose();
        foreach (var f in _fontCache.Values) f.Dispose();
        _penCache.Clear();
        _fontCache.Clear();
        _brush.Dispose();
        _fontFamily.Dispose();
    }
}
