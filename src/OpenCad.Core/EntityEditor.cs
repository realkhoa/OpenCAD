using ACadSharp.Entities;
using OpenCad.Core.Editing;

namespace OpenCad.Core;

/// <summary>
/// API tương thích ngược. Toàn bộ logic đã chuyển sang <see cref="EntityTransform"/>,
/// vốn hỗ trợ đầy đủ move / rotate / scale / mirror bằng ma trận affine.
/// </summary>
public static class EntityEditor
{
    public static bool Move(Entity entity, double dx, double dy)
        => EntityTransform.Move(entity, dx, dy);

    public static bool Apply(Entity entity, Geometry.Affine2 transform)
        => EntityTransform.Apply(entity, transform);

    public static bool SetLayer(Entity entity, ACadSharp.Tables.Layer? layer)
    {
        if (layer is null) return false;
        entity.Layer = layer;
        return true;
    }
}
