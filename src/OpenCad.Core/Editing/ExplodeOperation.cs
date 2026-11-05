using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// EXPLODE: bung một đối tượng phức hợp thành các đối tượng đơn giản hơn.
/// Insert → các entity trong block (đã áp vị trí/scale/xoay); LwPolyline → các đoạn thẳng.
/// </summary>
public static class ExplodeOperation
{
    /// <summary>
    /// Bung <paramref name="entity"/>. Trả về danh sách đối tượng con;
    /// rỗng nghĩa là loại này không bung được.
    /// </summary>
    public static List<Entity> Explode(Entity entity)
        => Explode(entity, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static List<Entity> Explode(Entity entity, HashSet<string> stack)
    {
        var result = new List<Entity>();

        switch (entity)
        {
            case Insert insert:
            {
                var block = insert.Block;

                if (block is null || block.Entities.Count == 0) return result;

                // Chặn block lồng chính nó (dữ liệu hỏng có thể gây đệ quy vô hạn)
                if (stack.Contains(block.Name)) return result;

                stack.Add(block.Name);

                var xf = InsertTransform(insert);

                foreach (var child in block.Entities)
                {
                    Entity? copy;

                    try { copy = child.Clone() as Entity; }
                    catch { continue; }

                    if (copy is null) continue;

                    try
                    {
                        if (!EntityTransform.Apply(copy, xf)) continue;
                    }
                    catch { continue; }

                    // Con của Insert thường nằm trên layer 0 → theo layer của Insert
                    try
                    {
                        if (copy.Layer is null || copy.Layer.Name == "0") copy.Layer = insert.Layer;
                    }
                    catch { }

                    // Block lồng → bung tiếp
                    if (copy is Insert nested)
                    {
                        var deeper = Explode(nested, stack);

                        if (deeper.Count > 0) result.AddRange(deeper);
                        else result.Add(copy);
                    }
                    else
                    {
                        result.Add(copy);
                    }
                }

                stack.Remove(block.Name);

                return result;
            }

            case LwPolyline polyline:
            {
                var points = polyline.Vertices
                    .Select(v => new Pt(v.Location.X, v.Location.Y))
                    .ToList();

                if (points.Count < 2) return result;

                var closed = polyline.IsClosed;

                for (var i = 0; i + 1 < points.Count; i++)
                    result.Add(MakeLine(points[i], points[i + 1], polyline));

                if (closed && points.Count > 2)
                    result.Add(MakeLine(points[^1], points[0], polyline));

                return result;
            }

            case MultiLeader multiLeader:
            {
                // MULTILEADER không sống sót qua bộ ghi DWG → bung ra thành line + chữ để giữ nội dung.
                var prims = new List<OpenCad.Core.Rendering.Prim>();

                try
                {
                    OpenCad.Core.Rendering.EntityFlattener.Flatten(
                        multiLeader, prims, Affine2.Identity, OpenCad.Core.Colors.Rgb.White, 0);
                }
                catch { return result; }

                return PrimsToEntities(prims, multiLeader);
            }

            case Dimension dimension:
            {
                // Dùng chính bộ vẽ để dựng hình của kích thước, rồi chuyển thành entity đơn.
                var prims = new List<OpenCad.Core.Rendering.Prim>();

                try
                {
                    OpenCad.Core.Rendering.EntityFlattener.Flatten(
                        dimension, prims, Affine2.Identity, OpenCad.Core.Colors.Rgb.White, 0);
                }
                catch { return result; }

                return PrimsToEntities(prims, dimension);
            }

            case MLine mline:
            {
                var points = mline.Vertices
                    .Select(v => new Pt(v.Position.X, v.Position.Y))
                    .ToList();

                for (var i = 0; i + 1 < points.Count; i++)
                    result.Add(MakeLine(points[i], points[i + 1], mline));

                return result;
            }
        }

        return result;
    }

    /// <summary>Ma trận biến đổi của một Insert (vị trí + scale + xoay).</summary>
    public static Affine2 InsertTransform(Insert insert)
    {
        var x = insert.InsertPoint.X;
        var y = insert.InsertPoint.Y;

        var sx = insert.XScale == 0 ? 1 : insert.XScale;
        var sy = insert.YScale == 0 ? 1 : insert.YScale;

        var rotation = insert.Rotation;

        // Dời về gốc → scale → xoay → dời tới điểm chèn
        // Scale đều (XScale) rồi xoay, sau đó dời tới điểm chèn
        var uniform = Math.Abs(sx) > 1e-12 ? sx : sy;

        return Affine2.Translate(x, y)
            .After(Affine2.Rotate(rotation, Pt.Zero))
            .After(Affine2.Scale(uniform, Pt.Zero));
    }

    /// <summary>Chuyển các primitive đã dựng thành entity đơn (dùng khi bung kích thước).</summary>
    private static List<Entity> PrimsToEntities(List<OpenCad.Core.Rendering.Prim> prims, Entity source)
    {
        var result = new List<Entity>();

        foreach (var prim in prims)
        {
            try
            {
                switch (prim.Kind)
                {
                    case OpenCad.Core.Rendering.PrimKind.Text:
                        result.Add(new TextEntity
                        {
                            InsertPoint = new XYZ(prim.Position.X, prim.Position.Y, 0),
                            Height = prim.Height > 0 ? prim.Height : 1,
                            Rotation = prim.Rotation,
                            Value = prim.Text ?? string.Empty,
                            Layer = source.Layer,
                            Color = source.Color,
                        });
                        break;

                    case OpenCad.Core.Rendering.PrimKind.Polyline:
                        for (var i = 0; i + 1 < prim.Points.Length; i++)
                        {
                            result.Add(MakeLine(prim.Points[i], prim.Points[i + 1], source));
                        }

                        if (prim.Closed && prim.Points.Length > 2)
                            result.Add(MakeLine(prim.Points[^1], prim.Points[0], source));

                        break;

                    case OpenCad.Core.Rendering.PrimKind.Hatch:
                    {
                        // Vùng tô (mũi tên): dựng lại thành polyline kín để giữ hình
                        var solid = new LwPolyline { IsClosed = true, Layer = source.Layer };

                        foreach (var point in prim.Points)
                            solid.Vertices.Add(new LwPolyline.Vertex { Location = new XY(point.X, point.Y) });

                        if (solid.Vertices.Count >= 3) result.Add(solid);
                        break;
                    }
                }
            }
            catch { }
        }

        return result;
    }

    private static Line MakeLine(Pt a, Pt b, Entity source)
    {
        return new Line
        {
            StartPoint = new XYZ(a.X, a.Y, 0),
            EndPoint = new XYZ(b.X, b.Y, 0),
            Layer = source.Layer,
            Color = source.Color,
            LineWeight = source.LineWeight,
            LineType = source.LineType,
        };
    }
}
