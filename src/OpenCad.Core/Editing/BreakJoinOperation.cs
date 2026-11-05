using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// BREAK: cắt một đối tượng tại hai điểm, bỏ đoạn giữa (hoặc tách thành hai đối tượng).
/// </summary>
public static class BreakOperation
{
    /// <summary>
    /// Cắt <paramref name="entity"/> giữa hai điểm. Trả về các đối tượng thay thế
    /// (rỗng nếu không cắt được). Đối tượng gốc do nơi gọi xoá.
    /// </summary>
    public static List<Entity> Apply(Entity entity, Pt from, Pt to)
    {
        var result = new List<Entity>(2);

        try
        {
            switch (entity)
            {
                case Line line:
                {
                    var start = new Pt(line.StartPoint.X, line.StartPoint.Y);
                    var end = new Pt(line.EndPoint.X, line.EndPoint.Y);

                    // Chiếu hai điểm cắt lên đường
                    var a = ProjectOnto(start, end, from);
                    var b = ProjectOnto(start, end, to);

                    var length = Pt.Distance(start, end);
                    if (length < 1e-9 || a is null || b is null) return result;

                    var t1 = Pt.Distance(start, a.Value);
                    var t2 = Pt.Distance(start, b.Value);

                    if (t1 > t2) (t1, t2) = (t2, t1);

                    if (t2 - t1 < 1e-9) return result;

                    if (t1 > 1e-6)
                        result.Add(new Line
                        {
                            StartPoint = line.StartPoint,
                            EndPoint = new XYZ(a.Value.X, a.Value.Y, line.StartPoint.Z),
                            Layer = line.Layer,
                            Color = line.Color,
                            LineWeight = line.LineWeight,
                            LineType = line.LineType,
                        });

                    if (t2 < length - 1e-6)
                        result.Add(new Line
                        {
                            StartPoint = new XYZ(b.Value.X, b.Value.Y, line.StartPoint.Z),
                            EndPoint = line.EndPoint,
                            Layer = line.Layer,
                            Color = line.Color,
                            LineWeight = line.LineWeight,
                            LineType = line.LineType,
                        });

                    return result;
                }

                case Arc arc:
                {
                    // Cắt cung: giữ phần ngoài hai điểm cắt (đơn giản hoá: cắt lấy phần đầu)
                    var center = new Pt(arc.Center.X, arc.Center.Y);

                    var angleFrom = Math.Atan2(from.Y - center.Y, from.X - center.X);
                    var angleTo = Math.Atan2(to.Y - center.Y, to.X - center.X);

                    var first = new Arc
                    {
                        Center = arc.Center,
                        Radius = arc.Radius,
                        StartAngle = arc.StartAngle,
                        EndAngle = angleFrom,
                        Layer = arc.Layer,
                        Color = arc.Color,
                        LineWeight = arc.LineWeight,
                        LineType = arc.LineType,
                    };

                    var second = new Arc
                    {
                        Center = arc.Center,
                        Radius = arc.Radius,
                        StartAngle = angleTo,
                        EndAngle = arc.EndAngle,
                        Layer = arc.Layer,
                        Color = arc.Color,
                        LineWeight = arc.LineWeight,
                        LineType = arc.LineType,
                    };

                    result.Add(first);
                    result.Add(second);

                    return result;
                }

                case Circle circle:
                {
                    // Cắt đường tròn tại hai điểm → cung chạy từ điểm thứ hai vòng lại điểm thứ nhất
                    var center = new Pt(circle.Center.X, circle.Center.Y);
                    var radius = circle.Radius;

                    if (radius < 1e-9) return result;

                    var startAngle = Math.Atan2(from.Y - center.Y, from.X - center.X);
                    var endAngle = Math.Atan2(to.Y - center.Y, to.X - center.X);

                    result.Add(new Arc
                    {
                        Center = circle.Center,
                        Radius = radius,
                        StartAngle = endAngle,
                        EndAngle = startAngle + Math.PI * 2,
                        Layer = circle.Layer,
                        Color = circle.Color,
                        LineWeight = circle.LineWeight,
                        LineType = circle.LineType,
                    });

                    return result;
                }

                case LwPolyline polyline:
                {
                    // Cắt polyline: chia thành hai polyline tại hai đỉnh gần nhất
                    var vertices = polyline.Vertices
                        .Select(v => new Pt(v.Location.X, v.Location.Y))
                        .ToList();

                    if (vertices.Count < 3) return result;

                    var i1 = NearestVertex(vertices, from);
                    var i2 = NearestVertex(vertices, to);

                    if (i1 == i2) return result;

                    var (lo, hi) = i1 < i2 ? (i1, i2) : (i2, i1);

                    var firstPart = vertices.Take(lo + 1).ToList();
                    var secondPart = vertices.Skip(hi).ToList();

                    if (firstPart.Count >= 2) result.Add(MakePolyline(firstPart, polyline, closed: false));
                    if (secondPart.Count >= 2) result.Add(MakePolyline(secondPart, polyline, closed: false));

                    return result;
                }
            }
        }
        catch { }

        return result;
    }

    private static Pt? ProjectOnto(Pt start, Pt end, Pt point)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 1e-12) return null;

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return new Pt(start.X + dx * t, start.Y + dy * t);
    }

    private static int NearestVertex(IReadOnlyList<Pt> vertices, Pt point)
    {
        var best = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < vertices.Count; i++)
        {
            var distance = Pt.Distance(vertices[i], point);

            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    private static LwPolyline MakePolyline(IReadOnlyList<Pt> points, LwPolyline source, bool closed)
    {
        var result = new LwPolyline
        {
            IsClosed = closed,
            Layer = source.Layer,
            Color = source.Color,
            LineWeight = source.LineWeight,
            LineType = source.LineType,
        };

        foreach (var point in points)
            result.Vertices.Add(new LwPolyline.Vertex { Location = new XY(point.X, point.Y) });

        return result;
    }
}

/// <summary>JOIN: nối các đoạn thẳng rời thành một polyline (khi chúng nối đầu-cuối).</summary>
public static class JoinOperation
{
    private const double Tolerance = 1e-6;

    /// <summary>
    /// Nối các line trong <paramref name="entities"/> nếu chúng tạo thành chuỗi liên tục.
    /// Trả về polyline mới, hoặc null nếu không nối được thành một chuỗi.
    /// </summary>
    public static LwPolyline? JoinLines(IReadOnlyList<Line> entities)
    {
        if (entities.Count < 2) return null;

        var remaining = entities
            .Select(l => new Segment(new Pt(l.StartPoint.X, l.StartPoint.Y),
                                     new Pt(l.EndPoint.X, l.EndPoint.Y), l))
            .ToList();

        // Bắt đầu từ đoạn đầu tiên
        var chain = new List<Pt> { remaining[0].Start, remaining[0].End };
        var used = new List<Segment> { remaining[0] };
        remaining.RemoveAt(0);

        var grew = true;

        while (grew && remaining.Count > 0)
        {
            grew = false;

            foreach (var segment in remaining.ToList())
            {
                var tail = chain[^1];
                var head = chain[0];

                if (Close(segment.Start, tail))
                {
                    chain.Add(segment.End);
                }
                else if (Close(segment.End, tail))
                {
                    chain.Add(segment.Start);
                }
                else if (Close(segment.End, head))
                {
                    chain.Insert(0, segment.Start);
                }
                else if (Close(segment.Start, head))
                {
                    chain.Insert(0, segment.End);
                }
                else
                {
                    continue;
                }

                used.Add(segment);
                remaining.Remove(segment);
                grew = true;
                break;
            }
        }

        if (used.Count < 2) return null;

        // Nếu còn đoạn chưa nối được thì không phải một chuỗi duy nhất
        if (remaining.Count > 0) return null;

        var result = new LwPolyline { Layer = entities[0].Layer };

        foreach (var point in chain)
            result.Vertices.Add(new LwPolyline.Vertex { Location = new XY(point.X, point.Y) });

        return result;
    }

    private static bool Close(Pt a, Pt b) => Pt.Distance(a, b) < Tolerance;

    private sealed record Segment(Pt Start, Pt End, Line Source);
}
