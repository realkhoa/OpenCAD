using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace OpenCad.Core.Audit;

/// <summary>Kết quả dọn dẹp: những gì sẽ bị xoá (hoặc đã xoá).</summary>
public sealed record PurgeResult(
    List<string> Layers,
    List<string> Blocks,
    List<string> LineTypes)
{
    public int Total => Layers.Count + Blocks.Count + LineTypes.Count;

    public bool IsEmpty => Total == 0;
}

/// <summary>
/// Dọn định nghĩa mồ côi — tương tự PURGE của AutoCAD:
/// layer / block / kiểu nét không còn đối tượng nào dùng tới.
/// </summary>
public static class DrawingPurge
{
    /// <summary>Liệt kê những gì có thể xoá, chưa xoá gì.</summary>
    public static PurgeResult Inspect(CadDocument document, string? currentLayer = null)
    {
        var layers = new List<string>();
        var blocks = new List<string>();
        var lineTypes = new List<string>();

        if (document is null) return new PurgeResult(layers, blocks, lineTypes);

        // ---- Layer: bỏ "0", layer hiện hành, layer đang được entity dùng
        var usedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in document.Entities)
        {
            if (entity.Layer is not null) usedLayers.Add(entity.Layer.Name);
        }

        foreach (var block in document.BlockRecords)
        {
            foreach (var entity in block.Entities)
            {
                if (entity.Layer is not null) usedLayers.Add(entity.Layer.Name);
            }
        }

        foreach (var layer in document.Layers)
        {
            if (string.Equals(layer.Name, "0", StringComparison.Ordinal)) continue;

            if (currentLayer is not null
                && string.Equals(layer.Name, currentLayer, StringComparison.OrdinalIgnoreCase))
                continue;

            if (usedLayers.Contains(layer.Name)) continue;

            layers.Add(layer.Name);
        }

        // ---- Block: không block nào tham chiếu, bỏ block hệ thống (*Model_Space…)
        var usedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in document.Entities)
        {
            if (entity is Insert insert && insert.Block is not null)
                usedBlocks.Add(insert.Block.Name);
        }

        foreach (var block in document.BlockRecords)
        {
            foreach (var entity in block.Entities)
            {
                if (entity is Insert nested && nested.Block is not null)
                    usedBlocks.Add(nested.Block.Name);
            }
        }

        // Block của LAYOUT (tờ in) luôn được dùng — thiếu bước này thì purge xoá sạch nội dung tờ
        foreach (var layout in document.Layouts)
        {
            try
            {
                if (layout.AssociatedBlock is not null)
                    usedBlocks.Add(layout.AssociatedBlock.Name);
            }
            catch { }
        }

        foreach (var block in document.BlockRecords)
        {
            if (block.Name.StartsWith("*", StringComparison.Ordinal)) continue;
            if (usedBlocks.Contains(block.Name)) continue;

            blocks.Add(block.Name);
        }

        // ---- Kiểu nét: không layer/entity nào dùng
        var usedLineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ByLayer", "ByBlock", "Continuous",
        };

        foreach (var layer in document.Layers)
        {
            if (layer.LineType is not null) usedLineTypes.Add(layer.LineType.Name);
        }

        foreach (var entity in document.Entities)
        {
            if (entity.LineType is not null) usedLineTypes.Add(entity.LineType.Name);
        }

        foreach (var block in document.BlockRecords)
        {
            foreach (var entity in block.Entities)
            {
                if (entity.LineType is not null) usedLineTypes.Add(entity.LineType.Name);
            }
        }

        foreach (var lineType in document.LineTypes)
        {
            if (usedLineTypes.Contains(lineType.Name)) continue;

            lineTypes.Add(lineType.Name);
        }

        return new PurgeResult(layers, blocks, lineTypes);
    }

    /// <summary>Xoá những gì <see cref="Inspect"/> liệt kê. Trả về số định nghĩa đã xoá.</summary>
    public static int Apply(CadDocument document, PurgeResult plan)
    {
        if (document is null || plan.IsEmpty) return 0;

        var removed = 0;

        foreach (var name in plan.Layers)
        {
            try
            {
                var layer = document.Layers.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (layer is null) continue;

                document.Layers.Remove(name);
                removed++;
            }
            catch { }
        }

        foreach (var name in plan.Blocks)
        {
            try
            {
                var block = document.BlockRecords.FirstOrDefault(
                    b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

                if (block is null) continue;

                document.BlockRecords.Remove(name);
                removed++;
            }
            catch { }
        }

        foreach (var name in plan.LineTypes)
        {
            try
            {
                var lineType = document.LineTypes.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (lineType is null) continue;

                document.LineTypes.Remove(name);
                removed++;
            }
            catch { }
        }

        return removed;
    }
}
