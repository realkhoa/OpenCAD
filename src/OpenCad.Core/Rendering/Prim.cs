using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Rendering;

public enum PrimKind
{
    /// <summary>Đa tuyến (đoạn thẳng nối các điểm).</summary>
    Polyline,
    /// <summary>Chuỗi văn bản tại Position, xoay Rotation (radian).</summary>
    Text,
    /// <summary>Điểm đơn lẻ.</summary>
    Point,
    /// <summary>Vùng tô (hatch): nhiều vòng khép kín, có thể lồng nhau làm lỗ.</summary>
    Hatch,
}

/// <summary>
/// Primitive hình học đã "làm phẳng" từ một entity CAD.
/// Cả renderer và hit-tester đều dùng chung cấu trúc này để tránh lệch hành vi.
/// </summary>
public sealed class Prim
{
    public PrimKind Kind { get; init; }
    public Pt[] Points { get; init; } = Array.Empty<Pt>();
    public bool Closed { get; init; }
    public bool Filled { get; init; }
    public Rgb Color { get; init; } = Rgb.White;

    /// <summary>
    /// True nếu các điểm là kết quả lấy mẫu một đường cong (tròn/cung/spline).
    /// Khi đó KHÔNG dùng từng đỉnh làm điểm snap endpoint/midpoint,
    /// vì đó chỉ là điểm lấy mẫu chứ không phải đỉnh thật của hình.
    /// </summary>
    public bool IsCurve { get; init; }

    /// <summary>Tâm đường tròn / cung / ellipse — dùng cho snap Center.</summary>
    public Pt? Center { get; init; }

    /// <summary>Bán kính (đường tròn/cung) — dùng cho snap Quadrant.</summary>
    public double Radius { get; init; }

    /// <summary>
    /// Mẫu nét đứt theo đơn vị bản vẽ, xen kẽ nét–khoảng (dương = nét, âm = khoảng).
    /// Null nghĩa là nét liền.
    /// </summary>
    public double[]? Dash
    {
        get => _dash;
        set => _dash = value;
    }

    private double[]? _dash;

    // Hatch
    /// <summary>Các vòng biên của vùng tô (vòng lồng nhau = lỗ, theo quy tắc even-odd).</summary>
    public Pt[][]? Loops { get; init; }
    public bool SolidFill { get; init; }
    public double PatternAngle { get; init; }
    public double PatternSpacing { get; init; } = 3;

    /// <summary>Độ đậm nét hiệu dụng (mm). Dùng khi in và khi bật hiển thị LWT.</summary>
    public double LineWeightMm { get; set; } = Styles.LineWeights.DefaultMm;

    /// <summary>Chỉ số màu ACI gốc (0 nếu là true color) — để map độ đậm khi in theo màu.</summary>
    public int AciIndex { get; set; }

    // Text
    public string Text { get; init; } = string.Empty;
    public Pt Position { get; init; }
    public double Height { get; init; } = 1;
    public double Rotation { get; init; }

    public static Prim Polyline(IReadOnlyList<Pt> pts, Rgb color, bool closed = false, bool filled = false)
        => new()
        {
            Kind = PrimKind.Polyline,
            Points = pts as Pt[] ?? pts.ToArray(),
            Color = color,
            Closed = closed,
            Filled = filled,
        };

    public static Prim Curve(IReadOnlyList<Pt> pts, Rgb color, Pt center, double radius,
                             bool closed = false, bool filled = false)
        => new()
        {
            Kind = PrimKind.Polyline,
            Points = pts as Pt[] ?? pts.ToArray(),
            Color = color,
            Closed = closed,
            Filled = filled,
            IsCurve = true,
            Center = center,
            Radius = radius,
        };

    public static Prim TextAt(string text, Pt pos, double height, double rotation, Rgb color)
        => new()
        {
            Kind = PrimKind.Text,
            Text = text,
            Position = pos,
            Height = height,
            Rotation = rotation,
            Color = color,
        };

    public static Prim PointAt(Pt pos, Rgb color)
        => new()
        {
            Kind = PrimKind.Point,
            Position = pos,
            Points = new[] { pos },
            Color = color,
        };

    public static Prim HatchRegion(IReadOnlyList<Pt[]> loops, Rgb color,
                                   bool solid, double patternAngle, double patternSpacing)
        => new()
        {
            Kind = PrimKind.Hatch,
            Loops = loops as Pt[][] ?? loops.ToArray(),
            Color = color,
            SolidFill = solid,
            PatternAngle = patternAngle,
            PatternSpacing = patternSpacing <= 0 ? 3 : patternSpacing,
            Filled = true,
            Closed = true,
        };

    public RectD Bounds()
    {
        if (Kind == PrimKind.Text)
        {
            var w = (Text?.Length ?? 0) * Height * 0.6;
            return new RectD(Position.X, Position.Y, Position.X + w, Position.Y + Height);
        }
        if (Kind == PrimKind.Point)
            return new RectD(Position.X, Position.Y, Position.X, Position.Y);

        if (Kind == PrimKind.Hatch && Loops is { Length: > 0 })
        {
            var bounds = RectD.Empty;
            foreach (var loop in Loops) bounds = bounds.Union(RectD.FromPoints(loop));
            return bounds;
        }

        return RectD.FromPoints(Points);
    }

    /// <summary>Kiểm tra điểm có nằm trong vùng tô / đa tuyến khép kín không.</summary>
    public bool ContainsPoint(Pt point)
    {
        if (Kind == PrimKind.Hatch && Loops is not null)
        {
            var inside = false;
            foreach (var loop in Loops)
                if (EvenOddInside(loop, point)) inside = !inside;
            return inside;
        }

        if (Kind == PrimKind.Polyline && Closed && Points.Length >= 3)
            return EvenOddInside(Points, point);

        if (Kind == PrimKind.Text)
            return TextContains(point);

        return false;
    }

    /// <summary>
    /// Chữ chưa có số đo font chính xác nên ước lượng hộp bao: rộng ≈ 0.62 × số ký tự × chiều cao,
    /// có xét góc xoay quanh điểm chèn.
    /// </summary>
    private bool TextContains(Pt point)
    {
        if (string.IsNullOrEmpty(Text)) return false;

        var dx = point.X - Position.X;
        var dy = point.Y - Position.Y;

        if (Math.Abs(Rotation) > 1e-9)
        {
            var cos = Math.Cos(-Rotation);
            var sin = Math.Sin(-Rotation);
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            dx = rx;
            dy = ry;
        }

        var width = Math.Max(Height * 0.5, Text.Length * Height * 0.62);
        var bottom = -Height * 0.30;
        var top = Height * 1.15;

        return dx >= -Height * 0.15 && dx <= width && dy >= bottom && dy <= top;
    }

    private static bool EvenOddInside(Pt[] loop, Pt point)
    {
        var inside = false;
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
        return inside;
    }
}
