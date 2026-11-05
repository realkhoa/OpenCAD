using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenCad.Core.Export;

/// <summary>Kiểu in của một lớp: độ đậm nét và có in đen hay giữ màu.</summary>
public sealed class LayerPlotStyle
{
    /// <summary>Độ đậm nét khi in (mm). Null = giữ độ đậm của đối tượng.</summary>
    public double? WeightMm { get; set; }

    /// <summary>In đen (true) hay giữ màu gốc (false).</summary>
    public bool Black { get; set; } = true;
}

/// <summary>
/// Bảng kiểu in theo lớp — thay thế thực dụng cho file .ctb (định dạng nhị phân độc quyền).
/// Định dạng văn bản, sửa được bằng Notepad:
/// <code>
/// ; chú thích
/// monochrome = true
/// default = 0.18
/// TUONG = 0.35
/// CUA = 0.25
/// CHU = 0.13, color
/// </code>
/// </summary>
public sealed class PlotStyleTable
{
    private readonly Dictionary<string, LayerPlotStyle> _layers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>In đơn sắc toàn bản vẽ (mặc định true như bản in kỹ thuật).</summary>
    public bool Monochrome { get; set; } = true;

    /// <summary>Độ đậm cho lớp không có trong bảng (mm).</summary>
    public double DefaultWeightMm { get; set; } = 0.18;

    /// <summary>Số lớp được khai báo.</summary>
    public int Count => _layers.Count;

    /// <summary>Tên các lớp đã khai báo.</summary>
    public IReadOnlyList<string> LayerNames => _layers.Keys.OrderBy(k => k).ToList();

    /// <summary>Lấy kiểu in của một lớp (null nếu không khai báo).</summary>
    public LayerPlotStyle? For(string? layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return null;

        return _layers.TryGetValue(layerName.Trim(), out var style) ? style : null;
    }

    /// <summary>Đặt kiểu in cho một lớp.</summary>
    public void Set(string layerName, double? weightMm, bool black = true)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return;

        _layers[layerName.Trim()] = new LayerPlotStyle { WeightMm = weightMm, Black = black };
    }

    /// <summary>
    /// Đọc bảng từ văn bản. Dòng bắt đầu bằng ';' hoặc '#' là chú thích.
    /// Dạng: <c>tên lớp = độ đậm[, color]</c>, hoặc <c>monochrome = true|false</c>, <c>default = 0.18</c>.
    /// </summary>
    public static PlotStyleTable Parse(string text)
    {
        var table = new PlotStyleTable();

        if (string.IsNullOrWhiteSpace(text)) return table;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0) continue;
            if (line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;

            var eq = line.IndexOf('=');

            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (key.Equals("monochrome", StringComparison.OrdinalIgnoreCase))
            {
                table.Monochrome = !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                                   && value != "0";
                continue;
            }

            if (key.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm > 0)
                    table.DefaultWeightMm = mm;

                continue;
            }

            // "0.35, color" → giữ màu; còn lại là in đen
            var parts = value.Split(',');

            double? weight = null;
            var black = true;

            if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var parsed) && parsed > 0)
                weight = parsed;

            if (parts.Length > 1 &&
                parts[1].Trim().StartsWith("color", StringComparison.OrdinalIgnoreCase))
                black = false;

            table.Set(key, weight, black);
        }

        return table;
    }

    /// <summary>Đọc bảng từ tệp. Trả về null nếu tệp không tồn tại.</summary>
    public static PlotStyleTable? LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            return Parse(File.ReadAllText(path));
        }
        catch { return null; }
    }
}
