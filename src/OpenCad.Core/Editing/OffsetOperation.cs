using ACadSharp.Entities;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Tạo entity song song cách đều (lệnh OFFSET).
/// Hướng offset xác định bởi điểm người dùng click.
/// </summary>
public static class OffsetOperation
{
    private const double Eps = 1e-9;

    public static Entity? Create(Entity source, double distance, Pt side)
    {
        if (distance <= Eps) return null;

        return source switch
        {
            Line line => OffsetLine(line, distance, side),
            Arc arc => OffsetArc(arc, distance, side),
            Circle circle => OffsetCircle(circle, distance, side),
            Ellipse ellipse => OffsetEllipse(ellipse, distance, side),
            LwPolyline polyline => OffsetPolyline(polyline, distance, side),
            _ => null,
        };
    }

    // --------------------------------------------------------------- ELLIPSE

    /// <summary>
    /// Offset ellipse: giữ tâm và hướng trục, tăng/giảm cả hai bán trục cùng một lượng.
    /// (Đây là xấp xỉ — offset đúng của ellipse không còn là ellipse.)
    /// </summary>
    private static Entity? OffsetEllipse(Ellipse ellipse, double distance, Pt side)
    {
        var center = ToPt(ellipse.Center);
        var majorVector = ToPt(ellipse.MajorAxisEndPoint);
        var major = majorVector.Length;
        if (major <= Eps) return null;

        var minor = major * ellipse.RadiusRatio;
        if (minor <= Eps) return null;

        // Bán kính ellipse theo hướng điểm click
        var toSide = side - center;
        var angle = Math.Atan2(toSide.Y, toSide.X) - Math.Atan2(majorVector.Y, majorVector.X);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var radiusAt = (major * minor) /
                       Math.Sqrt(minor * minor * cos * cos + major * major * sin * sin);

        var outward = toSide.Length > radiusAt;
        var delta = outward ? distance : -distance;

        var newMajor = major + delta;
        var newMinor = minor + delta;

        if (newMajor <= Eps || newMinor <= Eps) return null;

        var ratio = Math.Clamp(newMinor / newMajor, 1e-6, 1.0);

        // Phải kéo dài trục lớn theo tỉ lệ mới, nếu không ellipse chỉ dày lên chứ không lớn ra
        var axisScale = newMajor / major;

        var copy = new Ellipse
        {
            Center = ellipse.Center,
            MajorAxisEndPoint = new CSMath.XYZ(
                ellipse.MajorAxisEndPoint.X * axisScale,
                ellipse.MajorAxisEndPoint.Y * axisScale,
                ellipse.MajorAxisEndPoint.Z),
            RadiusRatio = ratio,
            StartParameter = ellipse.StartParameter,
            EndParameter = ellipse.EndParameter,
            Thickness = ellipse.Thickness,
            Normal = ellipse.Normal,
        };

        CopyCommon(ellipse, copy);
        return copy;
    }

    // ------------------------------------------------------------------ LINE

    private static Entity? OffsetLine(Line line, double distance, Pt side)
    {
        var a = ToPt(line.StartPoint);
        var b = ToPt(line.EndPoint);
        var dir = (b - a).Normalized();
        if (dir.LengthSquared < Eps) return null;

        // Pháp tuyến bên trái; dấu phụ thuộc điểm click nằm phía nào
        var normal = dir.Perp();
        var sign = Pt.Cross(dir, side - a) >= 0 ? 1 : -1;
        var shift = normal * (distance * sign);

        var copy = new Line
        {
            StartPoint = new CSMath.XYZ(a.X + shift.X, a.Y + shift.Y, line.StartPoint.Z),
            EndPoint = new CSMath.XYZ(b.X + shift.X, b.Y + shift.Y, line.EndPoint.Z),
            Thickness = line.Thickness,
            Normal = line.Normal,
        };

        CopyCommon(line, copy);
        return copy;
    }

    // ---------------------------------------------------------------- CIRCLE

    private static Entity? OffsetCircle(Circle circle, double distance, Pt side)
    {
        var center = ToPt(circle.Center);
        var outward = Pt.Distance(side, center) > circle.Radius;
        var radius = outward ? circle.Radius + distance : circle.Radius - distance;
        if (radius <= Eps) return null;

        var copy = new Circle
        {
            Center = circle.Center,
            Radius = radius,
            Thickness = circle.Thickness,
            Normal = circle.Normal,
        };

        CopyCommon(circle, copy);
        return copy;
    }

    private static Entity? OffsetArc(Arc arc, double distance, Pt side)
    {
        var center = ToPt(arc.Center);
        var outward = Pt.Distance(side, center) > arc.Radius;
        var radius = outward ? arc.Radius + distance : arc.Radius - distance;
        if (radius <= Eps) return null;

        var copy = new Arc
        {
            Center = arc.Center,
            Radius = radius,
            StartAngle = arc.StartAngle,
            EndAngle = arc.EndAngle,
            Thickness = arc.Thickness,
            Normal = arc.Normal,
        };

        CopyCommon(arc, copy);
        return copy;
    }

    // -------------------------------------------------------------- POLYLINE

    private static Entity? OffsetPolyline(LwPolyline polyline, double distance, Pt side)
    {
        var vertices = polyline.Vertices
            .Select(v => new Pt(v.Location.X, v.Location.Y))
            .ToList();

        if (vertices.Count < 2) return null;

        var closed = polyline.IsClosed;

        double sign;

        if (closed)
        {
            // Pháp tuyến trái chỉ hướng vào trong khi polygon chạy ngược chiều kim đồng hồ,
            // nên phải xét cả chiều polygon lẫn việc điểm click nằm trong hay ngoài.
            var counterClockwise = SignedArea(vertices) > 0;
            var inwardSign = counterClockwise ? 1 : -1;
            var inside = IsPointInside(vertices, side);
            sign = inside ? inwardSign : -inwardSign;
        }
        else
        {
            // Polyline hở: hướng do phía điểm click quyết định, giống OFFSET đường thẳng.
            var first = (vertices[1] - vertices[0]).Normalized();
            sign = Pt.Cross(first, side - vertices[0]) >= 0 ? 1 : -1;
        }

        var offset = OffsetPolygonPoints(vertices, distance * sign, closed);
        if (offset is null) return null;

        var copy = new LwPolyline
        {
            IsClosed = polyline.IsClosed,
            Elevation = polyline.Elevation,
            Normal = polyline.Normal,
            ConstantWidth = polyline.ConstantWidth,
        };

        foreach (var p in offset)
            copy.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(p.X, p.Y) });

        CopyCommon(polyline, copy);
        return copy;
    }

    /// <summary>Dịch từng đỉnh theo pháp tuyến, giao hai đường offset kề nhau (miter).</summary>
    private static List<Pt>? OffsetPolygonPoints(List<Pt> vertices, double distance, bool closed)
    {
        var n = vertices.Count;
        var result = new List<Pt>(n);

        var count = closed ? n : n - 1;

        for (var i = 0; i < n; i++)
        {
            var prevIndex = i - 1;
            var nextIndex = i + 1;

            if (!closed)
            {
                if (i == 0) { result.Add(OffsetSingle(vertices[0], vertices[1], distance)); continue; }
                if (i == n - 1) { result.Add(OffsetSingle(vertices[n - 1], vertices[n - 2], distance)); continue; }
            }
            else
            {
                if (prevIndex < 0) prevIndex = n - 1;
                if (nextIndex >= n) nextIndex = 0;
                if (i == 0) prevIndex = n - 1;
            }

            var current = vertices[i];
            var prev = vertices[prevIndex];
            var next = vertices[nextIndex];

            var p = MiterPoint(prev, current, next, distance);
            result.Add(p ?? current);
        }

        _ = count;
        return result;
    }

    private static Pt OffsetSingle(Pt a, Pt b, double distance)
    {
        var dir = (b - a).Normalized();
        return a + dir.Perp() * distance;
    }

    /// <summary>Giao điểm của hai đường offset của hai cạnh kề (bo góc kiểu miter).</summary>
    private static Pt? MiterPoint(Pt prev, Pt current, Pt next, double distance)
    {
        var d1 = (current - prev).Normalized();
        var d2 = (next - current).Normalized();
        if (d1.LengthSquared < Eps || d2.LengthSquared < Eps) return null;

        var n1 = d1.Perp();
        var n2 = d2.Perp();

        var p1 = current + n1 * distance;
        var p2 = current + n2 * distance;

        var denom = Pt.Cross(d1, d2);
        if (Math.Abs(denom) < 1e-7) return p1;   // gần như thẳng hàng

        var t = Pt.Cross(p2 - p1, d2) / denom;
        return p1 + d1 * t;
    }

    /// <summary>Diện tích có dấu (shoelace): dương = ngược chiều kim đồng hồ.</summary>
    private static double SignedArea(List<Pt> polygon)
    {
        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5;
    }

    private static bool IsPointInside(List<Pt> polygon, Pt point)    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            if (pi.Y > point.Y != pj.Y > point.Y)
            {
                var x = (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                if (point.X < x) inside = !inside;
            }
        }
        return inside;
    }

    private static Pt ToPt(CSMath.XYZ v) => new(v.X, v.Y);

    private static void CopyCommon(Entity source, Entity target)
    {
        try
        {
            target.Layer = source.Layer;
            target.Color = source.Color;
            target.LineType = source.LineType;
            target.LineTypeScale = source.LineTypeScale;
        }
        catch { }
    }
}
