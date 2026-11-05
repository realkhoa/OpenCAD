using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Editing;

/// <summary>Tạo các hình cơ bản và đo đạc.</summary>
public static class ShapeFactory
{
    /// <summary>Ellipse từ tâm, điểm cuối trục lớn và bán trục nhỏ.</summary>
    public static Ellipse? CreateEllipse(Pt center, Pt majorAxisEnd, double minorRadius)
    {
        var major = majorAxisEnd - center;
        var majorLength = major.Length;

        if (majorLength < 1e-9 || minorRadius < 1e-9) return null;

        return new Ellipse
        {
            Center = new XYZ(center.X, center.Y, 0),
            MajorAxisEndPoint = new XYZ(major.X, major.Y, 0),
            RadiusRatio = Math.Clamp(minorRadius / majorLength, 1e-6, 1.0),
            StartParameter = 0,
            EndParameter = 2 * Math.PI,
        };
    }

    /// <summary>Đa giác đều nội tiếp đường tròn tâm <paramref name="center"/> qua <paramref name="vertex"/>.</summary>
    public static LwPolyline? CreatePolygon(Pt center, Pt vertex, int sides)
    {
        if (sides < 3 || sides > 1024) return null;

        var radius = Pt.Distance(center, vertex);
        if (radius < 1e-9) return null;

        var startAngle = Math.Atan2(vertex.Y - center.Y, vertex.X - center.X);
        var step = 2 * Math.PI / sides;

        var polygon = new LwPolyline { IsClosed = true };

        for (var i = 0; i < sides; i++)
        {
            var angle = startAngle + step * i;
            polygon.Vertices.Add(new LwPolyline.Vertex
            {
                Location = new XY(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle)),
            });
        }

        return polygon;
    }

    /// <summary>Điểm cuối trục lớn suy ra từ bán kính lớn và góc xoay.</summary>
    public static Pt MajorAxisEnd(Pt center, double radius, double rotationRad)
        => new(center.X + radius * Math.Cos(rotationRad),
               center.Y + radius * Math.Sin(rotationRad));
}

/// <summary>
/// STRETCH: chỉ dịch những đỉnh nằm trong khung chọn, phần còn lại giữ nguyên.
/// Entity nằm trọn trong khung thì dịch cả; nằm ngoài thì không đổi.
/// </summary>
public static class StretchOperation
{
    /// <summary>Dịch các phần nằm trong <paramref name="window"/> theo <paramref name="delta"/>.</summary>
    public static bool Apply(Entity entity, RectD window, Pt delta)
    {
        switch (entity)
        {
            case Line line:
            {
                var moved = false;

                if (Inside(window, line.StartPoint))
                {
                    line.StartPoint = Offset(line.StartPoint, delta);
                    moved = true;
                }

                if (Inside(window, line.EndPoint))
                {
                    line.EndPoint = Offset(line.EndPoint, delta);
                    moved = true;
                }

                return moved;
            }

            case LwPolyline polyline:
            {
                var moved = false;

                foreach (var vertex in polyline.Vertices)
                {
                    if (!Inside(window, vertex.Location.X, vertex.Location.Y)) continue;

                    vertex.Location = new XY(vertex.Location.X + delta.X, vertex.Location.Y + delta.Y);
                    moved = true;
                }

                return moved;
            }

            case Arc arc:
                return MoveIfInside(window, ToPt(arc.Center), delta,
                    p => arc.Center = new CSMath.XYZ(p.X, p.Y, arc.Center.Z));

            case Circle circle:
                return MoveIfInside(window, ToPt(circle.Center), delta,
                    p => circle.Center = new CSMath.XYZ(p.X, p.Y, circle.Center.Z));

            case Ellipse ellipse:
                return MoveIfInside(window, ToPt(ellipse.Center), delta,
                    p => ellipse.Center = new CSMath.XYZ(p.X, p.Y, ellipse.Center.Z));

            case TextEntity text:
                return MoveIfInside(window, ToPt(text.InsertPoint), delta,
                    p => text.InsertPoint = new CSMath.XYZ(p.X, p.Y, text.InsertPoint.Z));

            case ACadSharp.Entities.Point point:
                return MoveIfInside(window, ToPt(point.Location), delta,
                    p => point.Location = new CSMath.XYZ(p.X, p.Y, point.Location.Z));

            case Insert insert:
                return MoveIfInside(window, ToPt(insert.InsertPoint), delta,
                    p => insert.InsertPoint = new CSMath.XYZ(p.X, p.Y, insert.InsertPoint.Z));

            default:
                return false;
        }
    }

    private static bool MoveIfInside(RectD window, Pt anchor, Pt delta, Action<Pt> setter)
    {
        if (!Inside(window, anchor.X, anchor.Y)) return false;

        setter(new Pt(anchor.X + delta.X, anchor.Y + delta.Y));
        return true;
    }

    private static bool Inside(RectD window, CSMath.XYZ v) => Inside(window, v.X, v.Y);

    private static bool Inside(RectD window, double x, double y)
    {
        var minX = Math.Min(window.MinX, window.MaxX);
        var maxX = Math.Max(window.MinX, window.MaxX);
        var minY = Math.Min(window.MinY, window.MaxY);
        var maxY = Math.Max(window.MinY, window.MaxY);

        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static CSMath.XYZ Offset(CSMath.XYZ v, Pt delta) => new(v.X + delta.X, v.Y + delta.Y, v.Z);

    private static Pt ToPt(CSMath.XYZ v) => new(v.X, v.Y);
}

/// <summary>
/// DIVIDE / MEASURE: chia một đối tượng thành các đoạn đều và trả về các điểm chia.
/// Dùng hình đã làm phẳng nên áp dụng được cho mọi loại entity.
/// </summary>
public static class MeasureOperation
{
    /// <summary>Chia thành <paramref name="count"/> đoạn — trả về count-1 điểm bên trong.</summary>
    public static List<Pt> Divide(Entity entity, int count)
    {
        if (count < 2) return new List<Pt>();

        var path = PathOf(entity);
        if (path.Count < 2) return new List<Pt>();

        var total = Length(path);
        if (total < 1e-9) return new List<Pt>();

        var result = new List<Pt>(count - 1);

        for (var i = 1; i < count; i++)
        {
            var point = PointAt(path, total * i / count);
            if (point is not null) result.Add(point.Value);
        }

        return result;
    }

    /// <summary>Đặt điểm cách nhau <paramref name="spacing"/> dọc theo đối tượng.</summary>
    public static List<Pt> Measure(Entity entity, double spacing)
    {
        if (spacing <= 1e-9) return new List<Pt>();

        var path = PathOf(entity);
        if (path.Count < 2) return new List<Pt>();

        var total = Length(path);
        if (total < spacing) return new List<Pt>();

        var result = new List<Pt>(Math.Min(10_000, (int)(total / spacing)));

        for (var distance = spacing; distance < total - 1e-9; distance += spacing)
        {
            var point = PointAt(path, distance);
            if (point is null) break;

            result.Add(point.Value);

            if (result.Count >= 10_000) break;
        }

        return result;
    }

    /// <summary>Đường gấp khúc của entity sau khi làm phẳng.</summary>
    private static List<Pt> PathOf(Entity entity)
    {
        var prims = new List<Prim>(8);

        try { EntityFlattener.Flatten(entity, prims, Affine2.Identity, Colors.Rgb.White, 0); }
        catch { return new List<Pt>(); }

        var longest = prims
            .Where(p => p.Points.Length >= 2)
            .OrderByDescending(p => p.Points.Length)
            .FirstOrDefault();

        return longest is null ? new List<Pt>() : longest.Points.ToList();
    }

    private static double Length(IReadOnlyList<Pt> path)
    {
        var total = 0.0;

        for (var i = 1; i < path.Count; i++)
            total += Pt.Distance(path[i - 1], path[i]);

        return total;
    }

    private static Pt? PointAt(IReadOnlyList<Pt> path, double distance)
    {
        var walked = 0.0;

        for (var i = 1; i < path.Count; i++)
        {
            var segment = Pt.Distance(path[i - 1], path[i]);

            if (segment < 1e-12) continue;

            if (walked + segment >= distance)
            {
                var t = (distance - walked) / segment;
                return Pt.Lerp(path[i - 1], path[i], t);
            }

            walked += segment;
        }

        return null;
    }
}

/// <summary>Đo khoảng cách và diện tích trên hình học đã làm phẳng.</summary>
public static class Measure
{
    public sealed record DistanceResult(double Distance, double AngleDegrees, double DeltaX, double DeltaY);

    public static DistanceResult Distance(Pt a, Pt b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        return new DistanceResult(distance, angle, dx, dy);
    }

    /// <summary>Diện tích của một entity khép kín (shoelace trên đa giác đã làm phẳng).</summary>
    public static double Area(Entity entity)
    {
        var prims = new List<Prim>(8);

        try { EntityFlattener.Flatten(entity, prims, Affine2.Identity, Colors.Rgb.White, 0); }
        catch { return 0; }

        var total = 0.0;

        foreach (var prim in prims)
        {
            switch (prim.Kind)
            {
                case PrimKind.Hatch when prim.Loops is not null:
                    foreach (var loop in prim.Loops)
                        total += Math.Abs(SignedArea(loop));
                    break;

                case PrimKind.Polyline when prim.Closed && prim.Points.Length >= 3:
                    total += Math.Abs(SignedArea(prim.Points));
                    break;
            }
        }

        return total;
    }

    private static double SignedArea(IReadOnlyList<Pt> points)
    {
        var area = 0.0;

        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5;
    }
}
