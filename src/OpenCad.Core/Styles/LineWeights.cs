using ACadSharp;
using ACadSharp.Entities;

namespace OpenCad.Core.Styles;

/// <summary>
/// Độ đậm nét vẽ. AutoCAD lưu lineweight theo 1/100 mm; khi in còn có thể
/// map theo màu (kiểu CTB) — bản vẽ kỹ thuật thường in monochrome với độ đậm theo màu gốc.
/// </summary>
public static class LineWeights
{
    public const double DefaultMm = 0.25;
    public const double MinMm = 0.05;
    public const double MaxMm = 2.11;

    /// <summary>Đổi giá trị enum (đơn vị 1/100 mm) ra mm. Giá trị âm = ByLayer/ByBlock/Default.</summary>
    public static double FromType(LineWeightType type)
    {
        var raw = (int)type;
        if (raw >= 0) return Math.Clamp(raw / 100.0, MinMm, MaxMm);
        return DefaultMm;
    }

    /// <summary>Độ đậm hiệu dụng của entity, có xét ByLayer.</summary>
    public static double Resolve(Entity entity)
    {
        try
        {
            var raw = (int)entity.LineWeight;

            if (raw >= 0) return Math.Clamp(raw / 100.0, MinMm, MaxMm);

            if (raw == -1)   // ByLayer
            {
                var layerRaw = (int)(entity.Layer?.LineWeight ?? LineWeightType.Default);
                if (layerRaw >= 0) return Math.Clamp(layerRaw / 100.0, MinMm, MaxMm);
            }
        }
        catch { }

        return DefaultMm;
    }

    /// <summary>
    /// Độ đậm in theo màu (bảng kiểu CTB đơn sắc): màu càng "đậm" thì nét càng dày.
    /// Đây là quy ước phổ biến của bản vẽ kỹ thuật Việt Nam.
    /// </summary>
    public static double FromAci(int aci) => aci switch
    {
        1 => 0.13,    // đỏ    — nét mảnh (kích thước, ghi chú)
        2 => 0.18,    // vàng  — nét mảnh
        3 => 0.25,    // xanh lá
        4 => 0.35,    // cyan  — nét trung bình
        5 => 0.50,    // xanh dương
        6 => 0.70,    // magenta — nét đậm (tường)
        7 => 0.35,    // trắng/đen
        8 => 0.13,
        9 => 0.18,
        _ => 0.18,
    };
}
