using ACadSharp.Entities;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

public sealed record CornerResult(Line First, Line Second, Entity Connector);

/// <summary>
/// FILLET (bo tròn góc) và CHAMFER (vát góc) giữa hai đường thẳng.
/// Kết quả gồm hai đường đã cắt ngắn và một cung/đoạn nối mới.
/// </summary>
public static class FilletChamfer
{
    private const double Eps = 1e-9;

    // ----------------------------------------------------------------- FILLET

    public static CornerResult? Fillet(Line first, Line second, double radius)
    {
        if (radius <= Eps) return null;
        if (!TrySetup(first, second, out var set)) return null;

        var half = set.Angle / 2;
        var tangentDistance = radius / Math.Tan(half);

        if (tangentDistance >= set.LengthA - Eps || tangentDistance >= set.LengthB - Eps) return null;

        var t1 = set.Corner + set.DirA * tangentDistance;
        var t2 = set.Corner + set.DirB * tangentDistance;

        var bisector = (set.DirA + set.DirB).Normalized();
        if (bisector.LengthSquared < Eps) return null;

        var center = set.Corner + bisector * (radius / Math.Sin(half));

        var startAngle = Math.Atan2(t1.Y - center.Y, t1.X - center.X);
        var endAngle = Math.Atan2(t2.Y - center.Y, t2.X - center.X);

        // Arc của ACadSharp luôn chạy CCW từ StartAngle tới EndAngle,
        // nên phải chọn thứ tự sao cho cung nhận được đúng cung bé.
        var expectedSweep = Math.PI - set.Angle;
        var ccw = Intersections.Normalize(endAngle - startAngle);

        var arc = new Arc
        {
            Center = new CSMath.XYZ(center.X, center.Y, 0),
            Radius = radius,
            StartAngle = Math.Abs(ccw - expectedSweep) < 1e-6 ? startAngle : endAngle,
            EndAngle = Math.Abs(ccw - expectedSweep) < 1e-6 ? endAngle : startAngle,
        };

        var newFirst = ShortenLine(first, set.CornerA, t1);
        var newSecond = ShortenLine(second, set.CornerB, t2);

        CopyCommon(first, newFirst);
        CopyCommon(second, newSecond);
        CopyCommon(first, arc);

        return new CornerResult(newFirst, newSecond, arc);
    }

    // ---------------------------------------------------------------- CHAMFER

    public static CornerResult? Chamfer(Line first, Line second, double distanceA, double distanceB)
    {
        if (distanceA <= Eps || distanceB <= Eps) return null;
        if (!TrySetup(first, second, out var set)) return null;

        if (distanceA >= set.LengthA - Eps || distanceB >= set.LengthB - Eps) return null;

        var t1 = set.Corner + set.DirA * distanceA;
        var t2 = set.Corner + set.DirB * distanceB;

        var connector = new Line
        {
            StartPoint = new CSMath.XYZ(t1.X, t1.Y, 0),
            EndPoint = new CSMath.XYZ(t2.X, t2.Y, 0),
        };

        var newFirst = ShortenLine(first, set.CornerA, t1);
        var newSecond = ShortenLine(second, set.CornerB, t2);

        CopyCommon(first, newFirst);
        CopyCommon(second, newSecond);
        CopyCommon(first, connector);

        return new CornerResult(newFirst, newSecond, connector);
    }

    // ---------------------------------------------------------------- SETUP

    private sealed class Setup
    {
        public Pt Corner;
        public Pt DirA, DirB;
        public Pt CornerA, CornerB;      // đầu mút của mỗi line gần góc nhất
        public double LengthA, LengthB;  // khoảng cách từ góc tới đầu mút xa
        public double Angle;
    }

    private static bool TrySetup(Line first, Line second, out Setup setup)
    {
        setup = new Setup();

        var a1 = Intersections.ToPt(first.StartPoint);
        var a2 = Intersections.ToPt(first.EndPoint);
        var b1 = Intersections.ToPt(second.StartPoint);
        var b2 = Intersections.ToPt(second.EndPoint);

        if (!InfiniteIntersect(a1, a2, b1, b2, out var corner)) return false;

        // Hướng ra xa góc: chọn đầu mút xa hơn làm phía giữ lại
        var farA = Pt.Distance(a1, corner) >= Pt.Distance(a2, corner) ? a1 : a2;
        var farB = Pt.Distance(b1, corner) >= Pt.Distance(b2, corner) ? b1 : b2;

        var dirA = (farA - corner).Normalized();
        var dirB = (farB - corner).Normalized();
        if (dirA.LengthSquared < Eps || dirB.LengthSquared < Eps) return false;

        var dot = Math.Clamp(Pt.Dot(dirA, dirB), -1, 1);
        var angle = Math.Acos(dot);

        if (angle < 1e-6 || Math.Abs(angle - Math.PI) < 1e-6) return false;   // song song / trùng nhau

        setup.Corner = corner;
        setup.DirA = dirA;
        setup.DirB = dirB;
        setup.CornerA = Pt.Distance(a1, corner) <= Pt.Distance(a2, corner) ? a1 : a2;
        setup.CornerB = Pt.Distance(b1, corner) <= Pt.Distance(b2, corner) ? b1 : b2;
        setup.LengthA = Pt.Distance(farA, corner);
        setup.LengthB = Pt.Distance(farB, corner);
        setup.Angle = angle;

        return true;
    }

    private static bool InfiniteIntersect(Pt a1, Pt a2, Pt b1, Pt b2, out Pt result)
    {
        result = Pt.Zero;

        var r = a2 - a1;
        var s = b2 - b1;
        var denom = Pt.Cross(r, s);
        if (Math.Abs(denom) < Eps) return false;

        var t = Pt.Cross(b1 - a1, s) / denom;
        result = a1 + r * t;
        return true;
    }

    /// <summary>Dời đầu mút gần góc về điểm cắt mới, giữ nguyên đầu kia.</summary>
    private static Line ShortenLine(Line source, Pt nearCorner, Pt newPoint)
    {
        var start = Intersections.ToPt(source.StartPoint);
        var end = Intersections.ToPt(source.EndPoint);

        var moveStart = Pt.Distance(start, nearCorner) <= Pt.Distance(end, nearCorner);

        return new Line
        {
            StartPoint = moveStart ? new CSMath.XYZ(newPoint.X, newPoint.Y, source.StartPoint.Z) : source.StartPoint,
            EndPoint = moveStart ? source.EndPoint : new CSMath.XYZ(newPoint.X, newPoint.Y, source.EndPoint.Z),
            Thickness = source.Thickness,
            Normal = source.Normal,
        };
    }

    // ------------------------------------------------------- POLYLINE

    /// <summary>
    /// Bo tròn **tất cả** góc của polyline. Góc nào không bo được (cạnh quá ngắn,
    /// gần thẳng hàng) thì giữ nguyên. Cung được lưu bằng bulge để vẫn là 1 polyline.
    /// </summary>
    public static LwPolyline? FilletAllCorners(LwPolyline source, double radius)
    {
        if (radius <= Eps) return null;

        var vertices = source.Vertices;
        if (vertices.Count < 3) return null;

        var points = vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
        var closed = source.IsClosed;

        var result = new LwPolyline
        {
            IsClosed = closed,
            Elevation = source.Elevation,
            Normal = source.Normal,
            ConstantWidth = source.ConstantWidth,
        };

        var start = closed ? 0 : 1;
        var end = closed ? points.Count - 1 : points.Count - 2;

        // Lượt 1: tính từng góc
        var corners = new (bool Ok, Pt T1, Pt T2, double Bulge, double Tangent)[points.Count];

        for (var i = 0; i < points.Count; i++)
        {
            if (i < start || i > end)
            {
                corners[i] = (false, points[i], points[i], 0, 0);
                continue;
            }

            var prev = points[(i - 1 + points.Count) % points.Count];
            var current = points[i];
            var next = points[(i + 1) % points.Count];

            corners[i] = TryCornerFillet(prev, current, next, radius,
                out var t1, out var t2, out var bulge, out var tangent)
                ? (true, t1, t2, bulge, tangent)
                : (false, current, current, 0, 0);
        }

        // Lượt 2: hai góc kề không được lấn vào nhau trên cùng một cạnh
        var edgeCount = closed ? points.Count : points.Count - 1;
        for (var i = 0; i < edgeCount; i++)
        {
            var a = i;
            var b = (i + 1) % points.Count;

            if (!corners[a].Ok || !corners[b].Ok) continue;

            var edgeLength = Pt.Distance(points[a], points[b]);
            if (corners[a].Tangent + corners[b].Tangent >= edgeLength - 1e-9)
            {
                corners[a] = corners[a] with { Ok = false };
                corners[b] = corners[b] with { Ok = false };
            }
        }

        var produced = new List<(Pt Point, double Bulge)>(points.Count * 2);

        if (!closed) produced.Add((points[0], 0));

        for (var i = 0; i < points.Count; i++)
        {
            if (!corners[i].Ok)
            {
                if (i == 0 || i == points.Count - 1)
                {
                    if (i != 0) produced.Add((points[i], 0));
                }
                else
                {
                    produced.Add((points[i], 0));
                }
                continue;
            }

            produced.Add((corners[i].T1, corners[i].Bulge));
            produced.Add((corners[i].T2, 0));
        }

        if (closed && produced.Count == 0) return null;

        foreach (var (point, bulge) in produced)
        {
            result.Vertices.Add(new LwPolyline.Vertex
            {
                Location = new CSMath.XY(point.X, point.Y),
                Bulge = bulge,
            });
        }

        CopyCommon(source, result);
        return result.Vertices.Count >= 3 ? result : null;
    }

    private static bool TryCornerFillet(Pt prev, Pt corner, Pt next, double radius,
                                        out Pt t1, out Pt t2, out double bulge, out double tangent)
    {
        t1 = corner;
        t2 = corner;
        bulge = 0;
        tangent = 0;

        var d1 = (prev - corner).Normalized();
        var d2 = (next - corner).Normalized();

        if (d1.LengthSquared < Eps || d2.LengthSquared < Eps) return false;

        var dot = Math.Clamp(Pt.Dot(d1, d2), -1, 1);
        var angle = Math.Acos(dot);

        if (angle < 1e-4 || Math.Abs(angle - Math.PI) < 1e-4) return false;   // thẳng hàng

        var distance = radius / Math.Tan(angle / 2);

        if (distance >= Pt.Distance(prev, corner) - Eps) return false;
        if (distance >= Pt.Distance(next, corner) - Eps) return false;

        t1 = corner + d1 * distance;
        t2 = corner + d2 * distance;
        tangent = distance;

        var sweep = Math.PI - angle;
        var sign = Pt.Cross(d1, d2) < 0 ? -1 : 1;
        bulge = sign * Math.Tan(sweep / 4);

        return true;
    }

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
