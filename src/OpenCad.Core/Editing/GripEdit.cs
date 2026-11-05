using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>Một điểm điều khiển (grip) của đối tượng đang chọn.</summary>
public sealed record Grip(int Index, Pt Position, string Kind);

/// <summary>
/// Điểm điều khiển để kéo trực tiếp bằng chuột — thay cho việc gõ lệnh MOVE/ROTATE.
/// Hỗ trợ các loại phổ biến; loại khác trả về danh sách rỗng.
/// </summary>
public static class GripSet
{
    /// <summary>Danh sách grip của entity (rỗng nếu không hỗ trợ).</summary>
    public static List<Grip> For(Entity entity)
    {
        var grips = new List<Grip>(8);

        try
        {
            switch (entity)
            {
                case Line line:
                    grips.Add(new Grip(0, ToPt(line.StartPoint), "start"));
                    grips.Add(new Grip(1, ToPt(line.EndPoint), "end"));
                    grips.Add(new Grip(2, Midpoint(ToPt(line.StartPoint), ToPt(line.EndPoint)), "mid"));
                    break;

                case Arc arc:
                    grips.Add(new Grip(0, ToPt(arc.Center), "center"));
                    grips.Add(new Grip(1, PointOnArc(arc, arc.StartAngle), "start"));
                    grips.Add(new Grip(2, PointOnArc(arc, arc.EndAngle), "end"));
                    break;

                case Circle circle:
                    grips.Add(new Grip(0, ToPt(circle.Center), "center"));
                    grips.Add(new Grip(1, new Pt(circle.Center.X + circle.Radius, circle.Center.Y), "radius"));
                    grips.Add(new Grip(2, new Pt(circle.Center.X, circle.Center.Y + circle.Radius), "radius"));
                    break;

                case TextEntity text:
                    grips.Add(new Grip(0, ToPt(text.InsertPoint), "insert"));
                    break;

                case MText mtext:
                    grips.Add(new Grip(0, ToPt(mtext.InsertPoint), "insert"));
                    break;

                case LwPolyline lw:
                    for (var i = 0; i < lw.Vertices.Count; i++)
                        grips.Add(new Grip(i, new Pt(lw.Vertices[i].Location.X, lw.Vertices[i].Location.Y), "vertex"));
                    break;

                case ACadSharp.Entities.Point point:
                    grips.Add(new Grip(0, ToPt(point.Location), "point"));
                    break;

                case Insert insert:
                    grips.Add(new Grip(0, ToPt(insert.InsertPoint), "insert"));
                    break;

                case Ellipse ellipse:
                    grips.Add(new Grip(0, ToPt(ellipse.Center), "center"));
                    break;

                case Leader leader when leader.Vertices is not null:
                    for (var i = 0; i < leader.Vertices.Count; i++)
                        grips.Add(new Grip(i, ToPt(leader.Vertices[i]), "vertex"));
                    break;

                case MLine mline when mline.Vertices is not null:
                    for (var i = 0; i < mline.Vertices.Count; i++)
                        grips.Add(new Grip(i, ToPt(mline.Vertices[i].Position), "vertex"));
                    break;
            }
        }
        catch { }

        return grips;
    }

    /// <summary>Grip gần điểm click nhất trong bán kính cho trước.</summary>
    public static Grip? Nearest(Entity entity, Pt world, double tolerance)
    {
        Grip? best = null;
        var bestDistance = tolerance;

        foreach (var grip in For(entity))
        {
            var distance = Pt.Distance(grip.Position, world);

            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = grip;
        }

        return best;
    }

    private static Pt ToPt(XYZ value) => new(value.X, value.Y);

    private static Pt Midpoint(Pt a, Pt b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static Pt PointOnArc(Arc arc, double angle) => new(
        arc.Center.X + arc.Radius * Math.Cos(angle),
        arc.Center.Y + arc.Radius * Math.Sin(angle));
}

/// <summary>Áp dụng kéo grip: đặt lại đúng điểm hình học tương ứng.</summary>
public static class GripEdit
{
    /// <summary>
    /// Chép hình học từ entity này sang entity khác cùng loại.
    /// Dùng cho undo/redo của thao tác kéo grip.
    /// </summary>
    public static bool CopyGeometry(Entity from, Entity to)
    {
        try
        {
            switch (from, to)
            {
                case (Line a, Line b):
                    b.StartPoint = a.StartPoint;
                    b.EndPoint = a.EndPoint;
                    return true;

                // Arc kế thừa Circle nên phải đứng trước
                case (Arc a, Arc b):
                    b.Center = a.Center;
                    b.Radius = a.Radius;
                    b.StartAngle = a.StartAngle;
                    b.EndAngle = a.EndAngle;
                    return true;

                case (Circle a, Circle b):
                    b.Center = a.Center;
                    b.Radius = a.Radius;
                    return true;

                case (Ellipse a, Ellipse b):
                    b.Center = a.Center;
                    b.MajorAxisEndPoint = a.MajorAxisEndPoint;
                    b.RadiusRatio = a.RadiusRatio;
                    return true;

                case (TextEntity a, TextEntity b):
                    b.InsertPoint = a.InsertPoint;
                    b.AlignmentPoint = a.AlignmentPoint;
                    b.Rotation = a.Rotation;
                    b.Height = a.Height;
                    return true;

                case (MText a, MText b):
                    b.InsertPoint = a.InsertPoint;
                    b.Height = a.Height;
                    return true;

                case (ACadSharp.Entities.Point a, ACadSharp.Entities.Point b):
                    b.Location = a.Location;
                    return true;

                case (Insert a, Insert b):
                    b.InsertPoint = a.InsertPoint;
                    b.Rotation = a.Rotation;
                    b.XScale = a.XScale;
                    b.YScale = a.YScale;
                    return true;

                case (Leader a, Leader b):
                {
                    if (a.Vertices is null || b.Vertices is null) return false;
                    if (a.Vertices.Count != b.Vertices.Count) return false;

                    for (var i = 0; i < a.Vertices.Count; i++) b.Vertices[i] = a.Vertices[i];

                    return true;
                }

                case (MLine a, MLine b):
                {
                    if (a.Vertices is null || b.Vertices is null) return false;
                    if (a.Vertices.Count != b.Vertices.Count) return false;

                    for (var i = 0; i < a.Vertices.Count; i++)
                        b.Vertices[i].Position = a.Vertices[i].Position;

                    return true;
                }

                case (LwPolyline a, LwPolyline b):
                {
                    if (a.Vertices.Count != b.Vertices.Count) return false;

                    for (var i = 0; i < a.Vertices.Count; i++)
                        b.Vertices[i].Location = a.Vertices[i].Location;

                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>Kéo grip <paramref name="grip"/> của entity tới điểm mới. Trả về true nếu đổi được.</summary>
    public static bool Move(Entity entity, Grip grip, Pt target)
    {
        try
        {
            switch (entity)
            {
                case Line line when grip.Kind == "start":
                    line.StartPoint = new XYZ(target.X, target.Y, line.StartPoint.Z);
                    return true;

                case Line line when grip.Kind == "end":
                    line.EndPoint = new XYZ(target.X, target.Y, line.EndPoint.Z);
                    return true;

                case Line line when grip.Kind == "mid":
                {
                    // Kéo giữa: dời cả đường
                    var middle = new Pt((line.StartPoint.X + line.EndPoint.X) / 2,
                                        (line.StartPoint.Y + line.EndPoint.Y) / 2);

                    var dx = target.X - middle.X;
                    var dy = target.Y - middle.Y;

                    line.StartPoint = new XYZ(line.StartPoint.X + dx, line.StartPoint.Y + dy, line.StartPoint.Z);
                    line.EndPoint = new XYZ(line.EndPoint.X + dx, line.EndPoint.Y + dy, line.EndPoint.Z);
                    return true;
                }

                case Arc arc when grip.Kind == "center":
                    arc.Center = new XYZ(target.X, target.Y, arc.Center.Z);
                    return true;

                case Circle circle when grip.Kind == "center":
                    circle.Center = new XYZ(target.X, target.Y, circle.Center.Z);
                    return true;

                case Circle circle when grip.Kind == "radius":
                {
                    var radius = Pt.Distance(
                        new Pt(circle.Center.X, circle.Center.Y), target);

                    if (radius < 1e-9) return false;

                    circle.Radius = radius;
                    return true;
                }

                case TextEntity text when grip.Kind == "insert":
                    text.InsertPoint = new XYZ(target.X, target.Y, text.InsertPoint.Z);
                    return true;

                case MText mtext when grip.Kind == "insert":
                    mtext.InsertPoint = new XYZ(target.X, target.Y, mtext.InsertPoint.Z);
                    return true;

                case LwPolyline lw when grip.Kind == "vertex" && grip.Index < lw.Vertices.Count:
                    lw.Vertices[grip.Index].Location = new XY(target.X, target.Y);
                    return true;

                case ACadSharp.Entities.Point point when grip.Kind == "point":
                    point.Location = new XYZ(target.X, target.Y, point.Location.Z);
                    return true;

                case Insert insert when grip.Kind == "insert":
                    insert.InsertPoint = new XYZ(target.X, target.Y, insert.InsertPoint.Z);
                    return true;

                case Ellipse ellipse when grip.Kind == "center":
                    ellipse.Center = new XYZ(target.X, target.Y, ellipse.Center.Z);
                    return true;

                case Leader leader when grip.Kind == "vertex"
                                         && leader.Vertices is not null
                                         && grip.Index < leader.Vertices.Count:
                    leader.Vertices[grip.Index] = new XYZ(target.X, target.Y, leader.Vertices[grip.Index].Z);
                    return true;

                case MLine mline when grip.Kind == "vertex"
                                      && mline.Vertices is not null
                                      && grip.Index < mline.Vertices.Count:
                    mline.Vertices[grip.Index].Position = new XYZ(
                        target.X, target.Y, mline.Vertices[grip.Index].Position.Z);
                    return true;
            }
        }
        catch { }

        return false;
    }
}
