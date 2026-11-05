using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace OpenCad.Core.Editing;

/// <summary>
/// IMPORT: nạp toàn bộ model space của một tệp DWG/DXF thành một block trong bản vẽ hiện tại.
/// Lớp của tệp nguồn được tạo theo tên nếu bản vẽ đích chưa có.
/// </summary>
public static class ImportOperation
{
    /// <summary>
    /// Nạp <paramref name="path"/> thành block tên <paramref name="blockName"/> trong <paramref name="target"/>.
    /// Trả về block vừa tạo (đã nằm trong bản vẽ), hoặc null nếu không nạp được.
    /// </summary>
    public static BlockRecord? Import(CadWorkspace target, string path, string? blockName = null)
    {
        if (target is null || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        CadWorkspace source;

        try { source = CadWorkspace.Open(path); }
        catch { return null; }

        var entities = source.Document.Entities.ToList();

        if (entities.Count == 0) return null;

        var document = target.Document;

        var name = string.IsNullOrWhiteSpace(blockName)
            ? Path.GetFileNameWithoutExtension(path)
            : blockName!.Trim();

        if (string.IsNullOrWhiteSpace(name)) name = "IMPORT";

        // Tên block không được trùng
        var unique = name;
        var suffix = 2;

        while (document.BlockRecords.Any(b => b.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)))
            unique = name + "-" + suffix++;

        BlockRecord block;

        try { block = new BlockRecord(unique); }
        catch { return null; }

        try { document.BlockRecords.Add(block); }
        catch { return null; }

        var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);

        // Lớp 0 chắc chắn có trong bản vẽ đích
        foreach (var layer in document.Layers) layers[layer.Name] = layer;

        foreach (var entity in entities)
        {
            Entity? copy;

            try { copy = entity.Clone() as Entity; }
            catch { continue; }

            if (copy is null) continue;

            // Lớp: dùng lại nếu trùng tên, tạo mới nếu chưa có
            try
            {
                var layerName = entity.Layer?.Name;

                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    if (!layers.TryGetValue(layerName, out var mapped))
                    {
                        mapped = new Layer(layerName)
                        {
                            Color = entity.Layer!.Color,
                            LineWeight = entity.Layer.LineWeight,
                            Flags = entity.Layer.Flags,
                        };

                        try
                        {
                            document.Layers.Add(mapped);
                            layers[layerName] = mapped;
                        }
                        catch { mapped = document.Layers.FirstOrDefault(); }

                        if (mapped is null) continue;
                    }

                    copy.Layer = mapped;
                }
            }
            catch { }

            try { block.Entities.Add(copy); }
            catch { }
        }

        return block.Entities.Count > 0 ? block : null;
    }
}
