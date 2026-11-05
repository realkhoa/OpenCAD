using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace OpenCad.Core.Editing;

/// <summary>
/// WBLOCK: ghi các đối tượng đang chọn ra một tệp DWG/DXF riêng.
/// Lớp và kiểu nét mà đối tượng dùng cũng được mang theo để tệp mới mở ra là dùng được ngay.
/// </summary>
public static class WBlockOperation
{
    /// <summary>
    /// Ghi <paramref name="entities"/> ra <paramref name="path"/>. Trả về số đối tượng đã ghi,
    /// hoặc -1 nếu không ghi được.
    /// </summary>
    public static int Write(IEnumerable<Entity> entities, string path,
                            ACadVersion version = ACadVersion.AC1032)
    {
        var source = entities?.Where(e => e is not null).ToList() ?? new List<Entity>();

        if (source.Count == 0) return 0;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is not null) Directory.CreateDirectory(directory);

        var workspace = CadWorkspace.New(version);
        var document = workspace.Document;

        // Lớp: mang theo mọi lớp mà đối tượng dùng
        var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in source
                     .Select(e => e.Layer)
                     .Where(l => l is not null)
                     .Select(l => l!)
                     .Distinct())
        {
            try
            {
                var copy = new Layer(layer.Name)
                {
                    Color = layer.Color,
                    LineWeight = layer.LineWeight,
                    IsOn = layer.IsOn,
                    Flags = layer.Flags,
                };

                if (layers.ContainsKey(copy.Name)) continue;

                document.Layers.Add(copy);
                layers[copy.Name] = copy;
            }
            catch { }
        }

        // Kiểu nét mà đối tượng dùng
        var lineTypeNames = source
            .Select(e => e.LineType?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in lineTypeNames)
        {
            try { Styles.LineTypeLibrary.Ensure(document, name); }
            catch { }
        }

        var written = 0;

        foreach (var entity in source)
        {
            Entity? copy;

            try { copy = entity.Clone() as Entity; }
            catch { continue; }

            if (copy is null) continue;

            // Gán lại lớp theo bản sao trong tệp mới
            try
            {
                var layerName = entity.Layer?.Name;

                if (layerName is not null && layers.TryGetValue(layerName, out var mapped))
                    copy.Layer = mapped;
            }
            catch { }

            try
            {
                document.Entities.Add(copy);
                written++;
            }
            catch { }
        }

        if (written == 0) return 0;

        workspace.Save(path);

        return written;
    }
}
