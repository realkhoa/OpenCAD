using OpenCad.Core.Geometry;

namespace OpenCad.App;

/// <summary>
/// Chuyển đổi giữa toạ độ world (CAD) và toạ độ màn hình (pixel).
/// Quy ước CAD: trục Y hướng lên; quy ước màn hình: trục Y hướng xuống.
/// </summary>
public sealed class ViewTransform
{
    public double Zoom { get; set; } = 1.0;
    public Pt Center { get; set; } = Pt.Zero;
    public int Width { get; set; }
    public int Height { get; set; }

    public PointF ToScreen(Pt p) => new(
        (float)((p.X - Center.X) * Zoom + Width * 0.5),
        (float)(Height * 0.5 - (p.Y - Center.Y) * Zoom));

    public Pt ToWorld(PointF s) => new(
        Center.X + (s.X - Width * 0.5) / Zoom,
        Center.Y - (s.Y - Height * 0.5) / Zoom);

    public Pt ToWorld(Point s) => ToWorld(new PointF(s.X, s.Y));

    /// <summary>Zoom giữ cố định điểm dưới con trỏ chuột.</summary>
    public void ZoomAt(PointF anchor, double factor)
    {
        var before = ToWorld(anchor);
        Zoom = Math.Clamp(Zoom * factor, 1e-9, 1e9);
        var after = ToWorld(anchor);
        Center += before - after;
    }

    public void PanByPixels(double dxPixels, double dyPixels)
    {
        Center = new Pt(Center.X - dxPixels / Zoom, Center.Y + dyPixels / Zoom);
    }

    /// <summary>Đưa toàn bộ bản vẽ vào khung nhìn.</summary>
    public void Fit(RectD bounds)
    {
        if (Width <= 0 || Height <= 0) return;

        if (bounds.IsEmpty)
        {
            Zoom = 1;
            Center = Pt.Zero;
            return;
        }

        const double margin = 0.06;
        var w = Math.Max(bounds.Width, 1e-9);
        var h = Math.Max(bounds.Height, 1e-9);

        if (w < 1e-6 && h < 1e-6)
        {
            // Bản vẽ chỉ có một điểm: chọn zoom hợp lý thay vì chia cho 0
            Zoom = 1;
            Center = bounds.Center;
            return;
        }

        var zx = Width * (1 - 2 * margin) / w;
        var zy = Height * (1 - 2 * margin) / h;
        var z = Math.Min(zx, zy);

        Zoom = Math.Clamp(z, 1e-9, 1e9);
        Center = bounds.Center;
    }

    public double PixelsToWorld(double pixels) => pixels / Zoom;
}
