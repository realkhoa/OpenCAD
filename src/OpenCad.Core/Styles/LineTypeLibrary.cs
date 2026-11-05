using ACadSharp;
using ACadSharp.Tables;

namespace OpenCad.Core.Styles;

/// <summary>
/// Thư viện kiểu nét chuẩn (tương đương các mục trong acad.lin).
/// Đơn vị tính theo mm bản vẽ, phù hợp bản vẽ kiến trúc tỷ lệ 1:50–1:100.
/// </summary>
public static class LineTypeLibrary
{
    /// <summary>Tên các kiểu nét có sẵn, theo thứ tự hiển thị.</summary>
    public static readonly string[] Names =
    {
        "Continuous", "DASHED", "HIDDEN", "CENTER", "DASHDOT", "PHANTOM", "DOT",
    };

    /// <summary>
    /// Bảo đảm document có kiểu nét theo tên. Trả về null nếu không tạo được.
    /// Kiểu "Continuous" luôn có sẵn trong mọi bản vẽ.
    /// </summary>
    public static LineType? Ensure(CadDocument document, string name)
    {
        if (document is null || string.IsNullOrWhiteSpace(name)) return null;

        var existing = document.LineTypes.FirstOrDefault(
            l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) return existing;

        var pattern = Pattern(name);
        if (pattern is null) return null;

        try
        {
            var lineType = new LineType(name) { Description = pattern.Value.Description };

            foreach (var (length, _) in pattern.Value.Segments)
                lineType.AddSegment(new LineType.Segment { Length = length });

            document.LineTypes.Add(lineType);

            return lineType;
        }
        catch { return null; }
    }

    /// <summary>Bảo đảm toàn bộ kiểu nét chuẩn có trong bản vẽ; trả về số kiểu vừa tạo.</summary>
    public static int EnsureAll(CadDocument document)
    {
        var created = 0;

        foreach (var name in Names)
        {
            var before = document.LineTypes.Count();
            Ensure(document, name);

            if (document.LineTypes.Count() > before) created++;
        }

        return created;
    }

    /// <summary>Danh sách kiểu nét của bản vẽ (bỏ ByLayer/ByBlock).</summary>
    public static IReadOnlyList<LineType> Available(CadDocument document)
    {
        try
        {
            return document.LineTypes
                .Where(l => !string.Equals(l.Name, "ByLayer", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(l.Name, "ByBlock", StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return Array.Empty<LineType>(); }
    }

    private static (string Description, (double Length, bool Dot)[] Segments)? Pattern(string name)
    {
        return name.ToUpperInvariant() switch
        {
            // Nét liền: không có segment nào
            "CONTINUOUS" => ("Solid line", Array.Empty<(double, bool)>()),

            "DASHED" => ("Dashed __ __ __", new (double, bool)[] { (300, false), (-150, false) }),

            "HIDDEN" => ("Hidden _ _ _", new (double, bool)[] { (150, false), (-75, false) }),

            "CENTER" => ("Center ____ _ ____", new (double, bool)[]
            {
                (900, false), (-120, false), (120, false), (-120, false),
            }),

            "DASHDOT" => ("Dash dot __ . __", new (double, bool)[]
            {
                (300, false), (-120, false), (1, false), (-120, false),
            }),

            "PHANTOM" => ("Phantom ___ _ _ ___", new (double, bool)[]
            {
                (900, false), (-150, false), (150, false), (-150, false),
                (150, false), (-150, false),
            }),

            "DOT" => ("Dot . . .", new (double, bool)[] { (1, false), (-150, false) }),

            _ => null,
        };
    }
}
