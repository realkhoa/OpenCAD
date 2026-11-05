using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Làm việc với block có trong bản vẽ: liệt kê block người dùng định nghĩa,
/// và tạo tham chiếu (INSERT) đặt vào model space.
/// </summary>
public static class BlockLibrary
{
    /// <summary>
    /// Các block người dùng định nghĩa. Bỏ qua block hệ thống: *Model_Space,
    /// *Paper_Space, block ẩn của dimension (*D…), block ẩn danh (*U…).
    /// </summary>
    public static IReadOnlyList<BlockRecord> Named(CadDocument document)
    {
        try
        {
            return document.BlockRecords
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .Where(b => !b.Name.StartsWith("*", StringComparison.Ordinal))
                .OrderBy(b => b.Name)
                .ToList();
        }
        catch
        {
            return Array.Empty<BlockRecord>();
        }
    }

    public static BlockRecord? Find(CadDocument document, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            return document.BlockRecords.FirstOrDefault(
                b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tạo tham chiếu block tại một điểm. Trả về null nếu block rỗng/không có.</summary>
    public static Insert? Create(BlockRecord block, Pt point, double scale = 1, double rotation = 0)
    {
        if (block is null) return null;
        if (scale <= 1e-9) return null;

        try
        {
            if (block.Entities.Count == 0) return null;
        }
        catch
        {
            return null;
        }

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(point.X, point.Y, 0),
            XScale = scale,
            YScale = scale,
            ZScale = scale,
            Rotation = rotation,
        };

        // ACadSharp tự sinh attribute từ ATTDEF của block nhưng đặt theo toạ độ
        // block-local. AutoCAD dùng toạ độ WCS nên phải dời về đúng vị trí.
        try
        {
            if (insert.Attributes is not null)
            {
                foreach (var attribute in insert.Attributes)
                {
                    if (attribute is null) continue;

                    attribute.InsertPoint = new XYZ(
                        point.X + attribute.InsertPoint.X * scale,
                        point.Y + attribute.InsertPoint.Y * scale,
                        attribute.InsertPoint.Z);
                }
            }
        }
        catch { }

        return insert;
    }

    /// <summary>Số entity bên trong block — để hiển thị khi liệt kê.</summary>
    public static int EntityCount(BlockRecord block)
    {
        try { return block.Entities.Count; }
        catch { return 0; }
    }
}
