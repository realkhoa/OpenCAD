using ACadSharp.Entities;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// TRIM (cắt bỏ đoạn giữa các giao điểm) và EXTEND (kéo dài tới biên).
/// Trả về entity mới thay vì sửa tại chỗ, để undo chỉ cần hoán đổi cũ/mới.
/// </summary>
public static class TrimExtendOperations
{
    private const double Eps = 1e-8;
    private const double MinPieceLength = 1e-6;

    // ------------------------------------------------------------------ TRIM

    /// <summary>
    /// Cắt phần chứa <paramref name="pick"/>. Trả về danh sách mảnh còn lại
    /// (rỗng nghĩa là xoá toàn bộ), null nghĩa là không cắt được.
    /// </summary>
    public static List<Entity>? Trim(Entity target, Pt pick, IReadOnlyList<Entity> boundaries)
    {
        return target switch
        {
            Line line => TrimLine(line, pick, boundaries),
            Arc arc => TrimArc(arc, pick, boundaries),
            Circle circle => TrimCircle(circle, pick, boundaries),
            _ => null,
        };
    }

    private static List<Entity>? TrimLine(Line line, Pt pick, IReadOnlyList<Entity> boundaries)
    {
        var a = Intersections.ToPt(line.StartPoint);
        var b = Intersections.ToPt(line.EndPoint);
        var dir = b - a;
        var lengthSq = dir.LengthSquared;

        if (lengthSq < Eps) return null;

        var cuts = new List<double>();
        foreach (var boundary in boundaries)
        {
            foreach (var p in Intersections.SegmentEntity(a, b, boundary))
            {
                var t = Pt.Dot(p - a, dir) / lengthSq;
                if (t > 1e-6 && t < 1 - 1e-6) cuts.Add(t);
            }
        }

        if (cuts.Count == 0) return null;

        cuts.Sort();
        cuts = Deduplicate(cuts);

        var pickParam = Math.Clamp(Pt.Dot(pick - a, dir) / lengthSq, 0, 1);

        var lower = 0.0;
        var upper = 1.0;
        foreach (var t in cuts)
        {
            if (t <= pickParam) lower = t;
            else { upper = t; break; }
        }

        var pieces = new List<Entity>(2);
        AddLinePiece(pieces, line, a, dir, 0, lower);
        AddLinePiece(pieces, line, a, dir, upper, 1);
        return pieces;
    }

    private static void AddLinePiece(List<Entity> output, Line source, Pt origin, Pt dir,
                                     double t0, double t1)
    {
        if (t1 - t0 <= 1e-6) return;

        var p0 = origin + dir * t0;
        var p1 = origin + dir * t1;
        if (Pt.Distance(p0, p1) < MinPieceLength) return;

        var piece = new Line
        {
            StartPoint = new CSMath.XYZ(p0.X, p0.Y, source.StartPoint.Z),
            EndPoint = new CSMath.XYZ(p1.X, p1.Y, source.EndPoint.Z),
            Thickness = source.Thickness,
            Normal = source.Normal,
        };

        CopyCommon(source, piece);
        output.Add(piece);
    }

    private static List<Entity>? TrimArc(Arc arc, Pt pick, IReadOnlyList<Entity> boundaries)
    {
        var center = Intersections.ToPt(arc.Center);
        var start = Intersections.Normalize(arc.StartAngle);
        var sweep = Intersections.Normalize(arc.EndAngle - arc.StartAngle);
        if (sweep < Eps) sweep = 2 * Math.PI;

        // Tham số 0..1 dọc theo cung
        var cuts = new List<double>();
        foreach (var boundary in boundaries)
        {
            foreach (var p in CircleEntity(center, arc.Radius, boundary))
            {
                var angle = Intersections.Normalize(Math.Atan2(p.Y - center.Y, p.X - center.X));
                var u = Intersections.Normalize(angle - start) / sweep;
                if (u > 1e-6 && u < 1 - 1e-6) cuts.Add(u);
            }
        }

        if (cuts.Count == 0) return null;

        cuts.Sort();
        cuts = Deduplicate(cuts);

        var pickAngle = Intersections.Normalize(Math.Atan2(pick.Y - center.Y, pick.X - center.X));
        var pickParam = Math.Clamp(Intersections.Normalize(pickAngle - start) / sweep, 0, 1);

        var lower = 0.0;
        var upper = 1.0;
        foreach (var t in cuts)
        {
            if (t <= pickParam) lower = t;
            else { upper = t; break; }
        }

        var pieces = new List<Entity>(2);
        AddArcPiece(pieces, arc, start, sweep, 0, lower);
        AddArcPiece(pieces, arc, start, sweep, upper, 1);
        return pieces;
    }

    private static List<Entity>? TrimCircle(Circle circle, Pt pick, IReadOnlyList<Entity> boundaries)
    {
        var center = Intersections.ToPt(circle.Center);

        var angles = new List<double>();
        foreach (var boundary in boundaries)
        {
            foreach (var p in CircleEntity(center, circle.Radius, boundary))
                angles.Add(Intersections.Normalize(Math.Atan2(p.Y - center.Y, p.X - center.X)));
        }

        // Cần ít nhất 2 giao điểm mới xác định được phần bị cắt của đường tròn kín
        if (angles.Count < 2) return null;

        angles.Sort();
        angles = Deduplicate(angles);

        var pickAngle = Intersections.Normalize(Math.Atan2(pick.Y - center.Y, pick.X - center.X));

        var index = -1;
        for (var i = 0; i < angles.Count; i++)
        {
            var current = angles[i];
            var next = angles[(i + 1) % angles.Count];
            var span = Intersections.Normalize(next - current);
            var offset = Intersections.Normalize(pickAngle - current);
            if (offset <= span) { index = i; break; }
        }

        if (index < 0) return null;

        // Phần còn lại là cung từ điểm cắt kế tiếp vòng về điểm cắt trước
        var from = angles[(index + 1) % angles.Count];
        var to = angles[index];

        var arc = new Arc
        {
            Center = circle.Center,
            Radius = circle.Radius,
            StartAngle = from,
            EndAngle = to,
            Normal = circle.Normal,
            Thickness = circle.Thickness,
        };

        CopyCommon(circle, arc);
        return new List<Entity> { arc };
    }

    private static void AddArcPiece(List<Entity> output, Arc source, double start, double sweep,
                                    double u0, double u1)
    {
        if (u1 - u0 <= 1e-6) return;

        var arc = new Arc
        {
            Center = source.Center,
            Radius = source.Radius,
            StartAngle = start + sweep * u0,
            EndAngle = start + sweep * u1,
            Normal = source.Normal,
            Thickness = source.Thickness,
        };

        CopyCommon(source, arc);
        output.Add(arc);
    }

    // ---------------------------------------------------------------- EXTEND

    /// <summary>Kéo dài đầu gần <paramref name="pick"/> tới biên gần nhất.</summary>
    public static Entity? Extend(Entity target, Pt pick, IReadOnlyList<Entity> boundaries)
    {
        if (target is not Line line) return null;

        var a = Intersections.ToPt(line.StartPoint);
        var b = Intersections.ToPt(line.EndPoint);

        var extendStart = Pt.Distance(pick, a) <= Pt.Distance(pick, b);

        var origin = extendStart ? a : b;
        var other = extendStart ? b : a;

        var direction = (origin - other).Normalized();
        if (direction.LengthSquared < Eps) return null;

        var searchRange = Math.Max(Pt.Distance(a, b) * 1000, 1e6);

        double? best = null;
        foreach (var boundary in boundaries)
        {
            var d = Intersections.RayEntityDistance(origin, direction, boundary, searchRange);
            if (d is { } value && (best is null || value < best)) best = value;
        }

        if (best is not { } distance) return null;

        var target2 = origin + direction * distance;

        var extended = new Line
        {
            StartPoint = extendStart ? new CSMath.XYZ(target2.X, target2.Y, line.StartPoint.Z) : line.StartPoint,
            EndPoint = extendStart ? line.EndPoint : new CSMath.XYZ(target2.X, target2.Y, line.EndPoint.Z),
            Thickness = line.Thickness,
            Normal = line.Normal,
        };

        CopyCommon(line, extended);
        return extended;
    }

    // --------------------------------------------------------------- HELPERS

    /// <summary>Giao điểm của một đường tròn đầy đủ với entity biên.</summary>
    private static List<Pt> CircleEntity(Pt center, double radius, Entity boundary)
    {
        var points = new List<Pt>(4);

        switch (boundary)
        {
            case Line line:
                points.AddRange(CircleSegment(center, radius,
                    Intersections.ToPt(line.StartPoint), Intersections.ToPt(line.EndPoint)));
                break;

            case Arc arc:
                foreach (var p in CircleCircle(center, radius,
                             Intersections.ToPt(arc.Center), arc.Radius))
                {
                    if (Intersections.AngleWithin(p, Intersections.ToPt(arc.Center),
                            arc.StartAngle, arc.EndAngle))
                        points.Add(p);
                }
                break;

            case Circle circle:
                points.AddRange(CircleCircle(center, radius,
                    Intersections.ToPt(circle.Center), circle.Radius));
                break;

            case LwPolyline lw:
            {
                var verts = lw.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
                AddPolylineCircle(center, radius, verts, lw.IsClosed, points);
                break;
            }

            case Polyline2D p2:
            {
                var verts = p2.Vertices.Select(v => new Pt(v.Location.X, v.Location.Y)).ToList();
                AddPolylineCircle(center, radius, verts, p2.IsClosed, points);
                break;
            }
        }

        return points;
    }

    private static void AddPolylineCircle(Pt center, double radius, List<Pt> verts, bool closed, List<Pt> output)
    {
        if (verts.Count < 2) return;

        var count = closed ? verts.Count : verts.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var c = verts[i];
            var d = verts[(i + 1) % verts.Count];
            output.AddRange(CircleSegment(center, radius, c, d));
        }
    }

    private static List<Pt> CircleSegment(Pt center, double radius, Pt a, Pt b)
    {
        var points = new List<Pt>(2);
        foreach (var t in Intersections.SegmentCircleParams(a, b, center, radius))
            points.Add(a + (b - a) * t);
        return points;
    }

    private static List<Pt> CircleCircle(Pt c1, double r1, Pt c2, double r2)
    {
        var result = new List<Pt>(2);

        var d = Pt.Distance(c1, c2);
        if (d < Eps || d > r1 + r2 + 1e-9 || d < Math.Abs(r1 - r2) - 1e-9) return result;

        var a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        var hSq = r1 * r1 - a * a;
        if (hSq < 0) return result;

        var h = Math.Sqrt(Math.Max(0, hSq));
        var dir = (c2 - c1) / d;
        var mid = c1 + dir * a;
        var perp = dir.Perp();

        result.Add(mid + perp * h);
        if (h > 1e-9) result.Add(mid - perp * h);

        return result;
    }

    private static List<double> Deduplicate(List<double> values, double tolerance = 1e-6)
    {
        var result = new List<double>(values.Count);
        foreach (var v in values)
        {
            if (result.Count == 0 || Math.Abs(v - result[^1]) > tolerance)
                result.Add(v);
        }
        return result;
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
