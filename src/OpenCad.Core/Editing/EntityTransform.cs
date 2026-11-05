using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Áp dụng một biến đổi affine lên entity.
/// Là cơ sở chung cho MOVE / COPY / ROTATE / SCALE / MIRROR — mỗi lệnh chỉ là
/// một ma trận khác nhau, nên không phải viết logic riêng cho từng lệnh.
/// </summary>
public static class EntityTransform
{
    private const double Eps = 1e-12;

    public static bool Apply(Entity entity, Affine2 m)
    {
        if (m.IsIdentity) return false;

        var s = m.ScaleFactor;
        var rot = m.RotationAngle;

        switch (entity)
        {
            case Line line:
                line.StartPoint = Map(line.StartPoint, m);
                line.EndPoint = Map(line.EndPoint, m);
                return true;

            case Ray ray:
                ray.StartPoint = Map(ray.StartPoint, m);
                ray.Direction = MapVector(ray.Direction, m);
                return true;

            case XLine xline:
                xline.FirstPoint = Map(xline.FirstPoint, m);
                xline.Direction = MapVector(xline.Direction, m);
                return true;

            case Arc arc:      // Arc kế thừa Circle nên phải đứng trước
                arc.Center = Map(arc.Center, m);
                arc.Radius *= s;
                arc.StartAngle += rot;
                arc.EndAngle += rot;
                return true;

            case Circle circle:
                circle.Center = Map(circle.Center, m);
                circle.Radius *= s;
                return true;

            case Ellipse ellipse:
                ellipse.Center = Map(ellipse.Center, m);
                ellipse.MajorAxisEndPoint = MapVector(ellipse.MajorAxisEndPoint, m);
                return true;

            case Point point:
                point.Location = Map(point.Location, m);
                return true;

            case Solid solid:
                solid.FirstCorner = Map(solid.FirstCorner, m);
                solid.SecondCorner = Map(solid.SecondCorner, m);
                solid.ThirdCorner = Map(solid.ThirdCorner, m);
                solid.FourthCorner = Map(solid.FourthCorner, m);
                return true;

            case TextEntity text:
                text.InsertPoint = Map(text.InsertPoint, m);

                // AlignmentPoint = (0,0,0) nghĩa là chưa dùng — nếu map nó sẽ thành toạ độ
                // thật và lấn át InsertPoint khi vẽ. Chỉ map khi đã được đặt.
                if (text.AlignmentPoint.X != 0 || text.AlignmentPoint.Y != 0 || text.AlignmentPoint.Z != 0)
                    text.AlignmentPoint = Map(text.AlignmentPoint, m);

                text.Height *= s;
                text.Rotation += rot;
                return true;

            case MText mtext:
                mtext.InsertPoint = Map(mtext.InsertPoint, m);
                // Lưu ý: MText.Rotation là read-only trong ACadSharp nên không xoay được chữ
                mtext.Height *= s;
                return true;

            case Insert insert:
                insert.InsertPoint = Map(insert.InsertPoint, m);
                insert.Rotation += rot;
                insert.XScale *= s;
                insert.YScale *= s;
                insert.ZScale *= s;
                return true;

            case LwPolyline lw:
                foreach (var v in lw.Vertices)
                {
                    var p = m.Apply(new Pt(v.Location.X, v.Location.Y));
                    v.Location = new XY(p.X, p.Y);
                }
                return true;

            case Polyline2D p2:
                foreach (var v in p2.Vertices) v.Location = Map(v.Location, m);
                return true;

            case Polyline3D p3:
                foreach (var v in p3.Vertices) v.Location = Map(v.Location, m);
                return true;

            case Spline spline:
                if (spline.ControlPoints is { Count: > 0 })
                    for (var i = 0; i < spline.ControlPoints.Count; i++)
                        spline.ControlPoints[i] = Map(spline.ControlPoints[i], m);
                if (spline.FitPoints is { Count: > 0 })
                    for (var i = 0; i < spline.FitPoints.Count; i++)
                        spline.FitPoints[i] = Map(spline.FitPoints[i], m);
                return true;

            case Hatch hatch:
                TransformHatch(hatch, m);
                return true;

            // Các dimension riêng phải đứng trước Dimension chung
            case DimensionAligned aligned:
                aligned.FirstPoint = Map(aligned.FirstPoint, m);
                aligned.SecondPoint = Map(aligned.SecondPoint, m);
                MapDimensionCommon(aligned, m);
                return true;

            case DimensionRadius radiusDim:
                radiusDim.AngleVertex = Map(radiusDim.AngleVertex, m);
                MapDimensionCommon(radiusDim, m);
                return true;

            case DimensionDiameter diameterDim:
                diameterDim.AngleVertex = Map(diameterDim.AngleVertex, m);
                MapDimensionCommon(diameterDim, m);
                return true;

            case Leader leader:
            {
                if (leader.Vertices is null) return false;

                for (var i = 0; i < leader.Vertices.Count; i++)
                    leader.Vertices[i] = Map(leader.Vertices[i], m);

                return true;
            }

            case MLine mline:
            {
                if (mline.Vertices is null) return false;

                foreach (var vertex in mline.Vertices)
                    vertex.Position = Map(vertex.Position, m);

                return true;
            }

            case Dimension dim:
                MapDimensionCommon(dim, m);
                return true;

            default:
                return false;
        }
    }

    private static void MapDimensionCommon(Dimension dim, Affine2 m)
    {
        dim.DefinitionPoint = Map(dim.DefinitionPoint, m);
        dim.InsertionPoint = Map(dim.InsertionPoint, m);
        dim.TextMiddlePoint = Map(dim.TextMiddlePoint, m);
    }

    /// <summary>Dịch/biến đổi các đường biên của hatch (line, polyline, arc).</summary>
    private static void TransformHatch(Hatch hatch, Affine2 m)
    {
        if (hatch.Paths is null) return;

        foreach (var path in hatch.Paths)
        {
            if (path.Edges is null) continue;

            foreach (var edge in path.Edges)
            {
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        line.Start = MapXY(line.Start, m);
                        line.End = MapXY(line.End, m);
                        break;

                    case Hatch.BoundaryPath.Polyline poly:
                        if (poly.Vertices is null) break;
                        for (var i = 0; i < poly.Vertices.Count; i++)
                            poly.Vertices[i] = Map(poly.Vertices[i], m);
                        break;

                    case Hatch.BoundaryPath.Arc arc:
                        arc.Center = MapXY(arc.Center, m);
                        arc.Radius *= m.ScaleFactor;
                        break;
                }
            }
        }

        if (hatch.SeedPoints is not null)
            for (var i = 0; i < hatch.SeedPoints.Count; i++)
                hatch.SeedPoints[i] = MapXY(hatch.SeedPoints[i], m);

        // Mẫu tô phải xoay/phóng theo hình, nếu không các vạch tô sẽ sai hướng
        if (Math.Abs(m.RotationAngle) > 1e-9)
            hatch.PatternAngle += m.RotationAngle;

        if (m.ScaleFactor > 1e-9 && Math.Abs(m.ScaleFactor - 1) > 1e-9)
            hatch.PatternScale *= m.ScaleFactor;
    }

    private static CSMath.XY MapXY(CSMath.XY v, Affine2 m)
    {
        var p = m.Apply(new Pt(v.X, v.Y));
        return new CSMath.XY(p.X, p.Y);
    }

    public static XYZ Map(XYZ v, Affine2 m)
    {
        var p = m.Apply(new Pt(v.X, v.Y));
        return new XYZ(p.X, p.Y, v.Z);
    }

    public static XYZ MapVector(XYZ v, Affine2 m)
    {
        var p = m.ApplyVector(new Pt(v.X, v.Y));
        return new XYZ(p.X, p.Y, v.Z);
    }

    /// <summary>Dịch chuyển — trường hợp riêng phổ biến nhất.</summary>
    public static bool Move(Entity entity, double dx, double dy)
    {
        if (Math.Abs(dx) < Eps && Math.Abs(dy) < Eps) return false;
        return Apply(entity, Affine2.Translate(dx, dy));
    }
}
