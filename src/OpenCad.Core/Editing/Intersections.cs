using ACadSharp.Entities;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Giao điểm chính xác giữa đoạn thẳng / cung tròn với các entity khác.
/// Dùng cho TRIM và EXTEND — phải chính xác, không được xấp xỉ bằng đa giác lấy mẫu.
/// </summary>
public static class Intersections
{
    private const double Eps = 1e-9;

    // ------------------------------------------------------- SEGMENT vs SEGMENT

    public static bool SegmentSegment(Pt a, Pt b, Pt c, Pt d, out Pt result, out double t)
    {
        result = Pt.Zero;
        t = 0;

        var r = b - a;
        var s = d - c;
        var denom = Pt.Cross(r, s);
        if (Math.Abs(denom) < Eps) return false;

        var qp = c - a;
        var u = Pt.Cross(qp, s) / denom;
        var v = Pt.Cross(qp, r) / denom;

        if (u < -1e-9 || u > 1 + 1e-9 || v < -1e-9 || v > 1 + 1e-9) return false;

        t = u;
        result = a + r * u;
        return true;
    }

    // ------------------------------------------------------- SEGMENT vs CIRCLE

    /// <summary>Trả về tham số t (0..1) trên đoạn a→b của các giao điểm với đường tròn.</summary>
    public static List<double> SegmentCircleParams(Pt a, Pt b, Pt center, double radius)
    {
        var result = new List<double>(2);
        var d = b - a;
        var f = a - center;

        var aa = Pt.Dot(d, d);
        if (aa < Eps) return result;

        var bb = 2 * Pt.Dot(f, d);
        var cc = Pt.Dot(f, f) - radius * radius;

        var disc = bb * bb - 4 * aa * cc;
        if (disc < 0) return result;

        if (disc < Eps)
        {
            var t = -bb / (2 * aa);
            if (t >= -1e-9 && t <= 1 + 1e-9) result.Add(Math.Clamp(t, 0, 1));
            return result;
        }

        var sqrt = Math.Sqrt(disc);
        var t1 = (-bb - sqrt) / (2 * aa);
        var t2 = (-bb + sqrt) / (2 * aa);

        if (t1 >= -1e-9 && t1 <= 1 + 1e-9) result.Add(Math.Clamp(t1, 0, 1));
        if (t2 >= -1e-9 && t2 <= 1 + 1e-9) result.Add(Math.Clamp(t2, 0, 1));

        return result;
    }

    // --------------------------------------------------------- SEGMENT vs ENTITY

    /// <summary>Giao điểm của đoạn a→b với một entity biên.</summary>
    public static List<Pt> SegmentEntity(Pt a, Pt b, Entity boundary)
    {
        var points = new List<Pt>(2);

        switch (boundary)
        {
            case Line line:
            {
                if (SegmentSegment(a, b, ToPt(line.StartPoint), ToPt(line.EndPoint), out var x, out _))
                    points.Add(x);
                break;
            }

            case Arc arc:                       // Arc kế thừa Circle → đứng trước
            {
                var center = ToPt(arc.Center);
                foreach (var t in SegmentCircleParams(a, b, center, arc.Radius))
                {
                    var p = a + (b - a) * t;
                    if (AngleWithin(p, center, arc.StartAngle, arc.EndAngle)) points.Add(p);
                }
                break;
            }

            case Circle circle:
            {
                var center = ToPt(circle.Center);
                foreach (var t in SegmentCircleParams(a, b, center, circle.Radius))
                    points.Add(a + (b - a) * t);
                break;
            }

            case LwPolyline lw:
            {
                var verts = lw.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
                AddPolylineIntersections(a, b, verts, lw.IsClosed, points);
                break;
            }

            case Polyline2D p2:
            {
                var verts = p2.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
                AddPolylineIntersections(a, b, verts, p2.IsClosed, points);
                break;
            }

            case Polyline3D p3:
            {
                var verts = p3.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
                AddPolylineIntersections(a, b, verts, p3.IsClosed, points);
                break;
            }
        }

        return points;
    }

    private static void AddPolylineIntersections(Pt a, Pt b, List<Pt> verts, bool closed, List<Pt> output)
    {
        if (verts.Count < 2) return;

        var count = closed ? verts.Count : verts.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var c = verts[i];
            var d = verts[(i + 1) % verts.Count];
            if (SegmentSegment(a, b, c, d, out var x, out _)) output.Add(x);
        }
    }

    /// <summary>Góc của điểm có nằm trong cung [start, end] (CCW) không.</summary>
    public static bool AngleWithin(Pt point, Pt center, double start, double end)
    {
        var angle = Math.Atan2(point.Y - center.Y, point.X - center.X);
        return Normalize(angle - start) <= Normalize(end - start) + 1e-9;
    }

    public static double Normalize(double angle)
    {
        var twoPi = 2 * Math.PI;
        angle %= twoPi;
        if (angle < 0) angle += twoPi;
        return angle;
    }

    // -------------------------------------------------------------- RAY vs ...

    /// <summary>
    /// Giao của tia (origin, direction) với entity biên, chỉ lấy phía trước tia.
    /// Trả về khoảng cách nhỏ nhất dương, hoặc null.
    /// </summary>
    public static double? RayEntityDistance(Pt origin, Pt direction, Entity boundary, double maxDistance)
    {
        var far = origin + direction * maxDistance;
        var best = (double?)null;

        switch (boundary)
        {
            case Line line:
            {
                if (SegmentSegment(origin, far, ToPt(line.StartPoint), ToPt(line.EndPoint), out var x, out _))
                {
                    var d = Pt.Distance(origin, x);
                    if (d > 1e-7) best = d;
                }
                break;
            }

            case Arc arc:
            {
                var center = ToPt(arc.Center);
                foreach (var t in SegmentCircleParams(origin, far, center, arc.Radius))
                {
                    var p = origin + (far - origin) * t;
                    if (!AngleWithin(p, center, arc.StartAngle, arc.EndAngle)) continue;
                    var d = Pt.Distance(origin, p);
                    if (d > 1e-7 && (best is null || d < best)) best = d;
                }
                break;
            }

            case Circle circle:
            {
                var center = ToPt(circle.Center);
                foreach (var t in SegmentCircleParams(origin, far, center, circle.Radius))
                {
                    var p = origin + (far - origin) * t;
                    var d = Pt.Distance(origin, p);
                    if (d > 1e-7 && (best is null || d < best)) best = d;
                }
                break;
            }

            default:
            {
                // Biên là polyline: dùng từng đoạn
                var verts = PolylineVertices(boundary);
                if (verts is null) break;

                var count = IsClosed(boundary) ? verts.Count : verts.Count - 1;
                for (var i = 0; i < count; i++)
                {
                    var c = verts[i];
                    var d = verts[(i + 1) % verts.Count];
                    if (!SegmentSegment(origin, far, c, d, out var x, out _)) continue;

                    var dist = Pt.Distance(origin, x);
                    if (dist > 1e-7 && (best is null || dist < best)) best = dist;
                }
                break;
            }
        }

        return best;
    }

    private static List<Pt>? PolylineVertices(Entity entity) => entity switch
    {
        LwPolyline lw => lw.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList(),
        Polyline2D p2 => p2.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList(),
        Polyline3D p3 => p3.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList(),
        _ => null,
    };

    private static bool IsClosed(Entity entity) => entity switch
    {
        LwPolyline lw => lw.IsClosed,
        Polyline2D p2 => p2.IsClosed,
        Polyline3D p3 => p3.IsClosed,
        _ => false,
    };

    public static Pt ToPt(CSMath.XYZ v) => new(v.X, v.Y);
}
