namespace OpenCad.Core.Commands;

/// <summary>Một lệnh trong bảng lệnh: tên chuẩn, các tên viết tắt, nhóm và mô tả.</summary>
public sealed record CommandInfo(string Name, string[] Aliases, string Category, string Description)
{
    /// <summary>Tất cả tên gọi được của lệnh (tên chuẩn + viết tắt).</summary>
    public IEnumerable<string> All => new[] { Name }.Concat(Aliases);
}

/// <summary>
/// Bảng lệnh của chương trình. Tập trung một chỗ để:
/// – phát hiện trùng tên viết tắt (lỗi từng gặp với PLINE/PL),
/// – sinh danh sách HELP,
/// – đối chiếu khi thêm lệnh mới.
/// </summary>
public static class CommandRegistry
{
    public static readonly CommandInfo[] All =
    {
        // ---- Vẽ
        new("LINE", new[] { "L" }, "Vẽ", "Vẽ đoạn thẳng liên tiếp"),
        new("PLINE", new[] { "PL" }, "Vẽ", "Vẽ polyline"),
        new("RECTANG", new[] { "REC" }, "Vẽ", "Vẽ hình chữ nhật"),
        new("CIRCLE", new[] { "C" }, "Vẽ", "Vẽ đường tròn"),
        new("ARC", new[] { "A" }, "Vẽ", "Vẽ cung 3 điểm"),
        new("ELLIPSE", new[] { "EL" }, "Vẽ", "Vẽ ellipse"),
        new("XLINE", new[] { "XL" }, "Vẽ", "Đường dựng vô hạn qua 2 điểm"),
        new("RAY", Array.Empty<string>(), "Vẽ", "Tia vô hạn từ 1 điểm theo hướng"),
        new("POLYGON", new[] { "POL" }, "Vẽ", "Vẽ đa giác đều"),
        new("SPLINE", new[] { "SPL" }, "Vẽ", "Vẽ đường cong qua các điểm click"),
        new("TABLE", new[] { "TB" }, "Chèn", "Tạo bảng (hỏi số hàng,cột rồi click điểm chèn)"),
        new("MLEADER", new[] { "MLD" }, "Chú thích", "Chú thích có đường dẫn: click mũi tên, click chữ, nhập nội dung"),
        new("POINT", new[] { "PO" }, "Vẽ", "Đặt điểm"),
        new("MLINE", new[] { "ML" }, "Vẽ", "Tường nhiều nét"),
        new("TEXT", new[] { "T", "DT" }, "Vẽ", "Đặt chữ một dòng"),
        new("MTEXT", new[] { "MT" }, "Vẽ", "Đặt chữ nhiều dòng"),
        new("HATCH", new[] { "H" }, "Vẽ", "Tô vật liệu"),
        new("HATCHEDIT", new[] { "HE" }, "Sửa", "Đổi tỷ lệ / góc / mẫu tô"),
        new("DIVIDE", new[] { "DIV" }, "Vẽ", "Chia đối tượng thành N đoạn"),
        new("MEASURE", new[] { "ME" }, "Vẽ", "Đặt điểm cách đều"),
        new("INSERT", new[] { "I" }, "Vẽ", "Chèn block"),
        new("PTEXT", new[] { "PT" }, "Vẽ", "Ghi chú trên tờ in"),
        new("SHEETLINE", new[] { "SL" }, "Vẽ", "Vẽ đường trên tờ in"),
        new("ATTDEF", new[] { "AD" }, "Vẽ", "Thêm attribute vào block"),

        // ---- Sửa
        new("MOVE", new[] { "M" }, "Sửa", "Dời đối tượng"),
        new("COPY", new[] { "CO", "CP" }, "Sửa", "Sao chép đối tượng"),
        new("ROTATE", new[] { "RO" }, "Sửa", "Xoay đối tượng"),
        new("SCALE", new[] { "SC" }, "Sửa", "Phóng to/thu nhỏ"),
        new("MIRROR", new[] { "MI" }, "Sửa", "Lật đối xứng"),
        new("OFFSET", new[] { "O" }, "Sửa", "Tạo đường song song"),
        new("TRIM", new[] { "TR" }, "Sửa", "Cắt phần thừa"),
        new("EXTEND", new[] { "EX" }, "Sửa", "Kéo dài tới đường biên"),
        new("FILLET", new[] { "F" }, "Sửa", "Bo tròn góc"),
        new("CHAMFER", new[] { "CHA" }, "Sửa", "Vát góc"),
        new("ERASE", new[] { "E", "DEL" }, "Sửa", "Xoá đối tượng"),
        new("ARRAY", new[] { "AR" }, "Sửa", "Dãy chữ nhật"),
        new("ARRAYPOLAR", new[] { "ARP" }, "Sửa", "Dãy tròn"),
        new("STRETCH", new[] { "S" }, "Sửa", "Kéo giãn"),
        new("ATTEDIT", new[] { "ATT" }, "Sửa", "Sửa giá trị attribute"),
        new("DDEDIT", new[] { "ED", "TEDIT" }, "Sửa", "Sửa nội dung chữ"),
        new("MATCHPROP", new[] { "MA" }, "Sửa", "Chép thuộc tính từ đối tượng nguồn"),
        new("COPYCLIP", Array.Empty<string>(), "Sửa", "Copy lựa chọn (Ctrl+C)"),
        new("PASTECLIP", Array.Empty<string>(), "Sửa", "Dán từ clipboard (Ctrl+V)"),
        new("LAYISO", Array.Empty<string>(), "Xem", "Ẩn mọi layer trừ layer của đối tượng đang chọn"),
        new("LAYDEL", Array.Empty<string>(), "Sửa", "Xoá layer đang chọn VÀ mọi đối tượng trên đó"),
        new("LAYUNISO", Array.Empty<string>(), "Xem", "Bật lại mọi layer"),
        new("DIMTEDIT", Array.Empty<string>(), "Kích thước", "Ghi đè chữ số đo của kích thước"),

        // ---- Kích thước
        new("DIMLINEAR", new[] { "DLI", "DIM" }, "Kích thước", "Kích thước thẳng đứng/ngang"),
        new("DIMALIGNED", new[] { "DAL" }, "Kích thước", "Kích thước song song"),
        new("DIMANGULAR", new[] { "DAN", "DIMANG" }, "Kích thước", "Kích thước góc"),
        new("DIMRADIUS", new[] { "DRA", "DIMRAD" }, "Kích thước", "Kích thước bán kính"),
        new("DIMDIAMETER", new[] { "DDI", "DIMDIA" }, "Kích thước", "Kích thước đường kính"),
        new("DIMORDINATE", new[] { "DOR", "DIMORD" }, "Kích thước", "Kích thước toạ độ"),
        new("DIMCONTINUE", new[] { "DCO" }, "Kích thước", "Nối tiếp chuỗi kích thước"),
        new("DIMBASELINE", new[] { "DBA" }, "Kích thước", "Kích thước song song từ một gốc"),
        new("DIMSTYLE", new[] { "DST" }, "Kích thước", "Đổi kiểu kích thước"),
        new("DIST", new[] { "DI" }, "Đo", "Đo khoảng cách"),
        new("AREA", new[] { "AA" }, "Đo", "Đo diện tích"),

        // ---- Block
        new("BLOCK", new[] { "B" }, "Block", "Tạo block từ lựa chọn"),
        new("BLOCKS", new[] { "BL" }, "Block", "Liệt kê block"),

        // ---- Xem
        new("ZOOM", new[] { "Z" }, "Xem", "Phóng to/thu nhỏ"),
        new("ZOOMALL", new[] { "ZA" }, "Xem", "Xem toàn bản vẽ"),
        new("ZOOMWINDOW", new[] { "ZW" }, "Xem", "Phóng tới vùng chọn bằng 2 click"),
        new("ZOOMPREVIOUS", new[] { "ZP" }, "Xem", "Quay lại khung nhìn trước"),
        new("OSNAP", new[] { "OS" }, "Xem", "Bật/tắt bắt điểm"),
        new("ORTHO", Array.Empty<string>(), "Xem", "Bật/tắt chế độ trực giao"),
        new("POLAR", Array.Empty<string>(), "Xem", "Bật/tắt rãnh căn 15°"),

        // ---- Tệp
        new("NEW", Array.Empty<string>(), "Tệp", "Bản vẽ mới"),
        new("OPEN", Array.Empty<string>(), "Tệp", "Mở bản vẽ"),
        new("SAVE", new[] { "QSAVE" }, "Tệp", "Lưu"),
        new("SAVEAS", Array.Empty<string>(), "Tệp", "Lưu thành tệp mới"),
        new("PDF", new[] { "EXPORTPDF" }, "Tệp", "Xuất PDF"),
        new("PLOT", Array.Empty<string>(), "Tệp", "In"),
        new("SKIP", new[] { "UNSUPPORTED" }, "Tệp", "Liệt kê đối tượng chưa hỗ trợ"),

        // ---- Khác
        new("UNDO", new[] { "U" }, "Khác", "Hoàn tác (U 5 = lùi 5 bước)"),
        new("OOPS", Array.Empty<string>(), "Sửa", "Phục hồi đối tượng vừa xoá"),
        new("EXPLODE", new[] { "X" }, "Sửa", "Bung block / polyline thành đối tượng đơn"),
        new("JOIN", new[] { "J" }, "Sửa", "Nối các đoạn thẳng thành một polyline"),
        new("SELLAYER", Array.Empty<string>(), "Sửa", "Chọn mọi đối tượng trên một lớp"),
        new("SELTYPE", Array.Empty<string>(), "Sửa", "Chọn mọi đối tượng cùng loại"),
        new("SELINVERT", Array.Empty<string>(), "Sửa", "Đảo lựa chọn"),
        new("SELECTUNDER", new[] { "SELALLD" }, "Sửa", "Chọn mọi đối tượng chồng nhau dưới con trỏ"),
        new("ZOOMOBJ", new[] { "ZO" }, "Xem", "Phóng tới các đối tượng đang chọn"),
        new("REDO", Array.Empty<string>(), "Khác", "Làm lại"),
        new("SELECTALL", new[] { "SELECT" }, "Khác", "Chọn tất cả"),
        new("VPORT", new[] { "VP" }, "Khác", "Tạo viewport trên tờ"),
        new("VPSCALE", new[] { "VS" }, "Khác", "Đổi tỷ lệ viewport (50 = 1:50)"),
        new("LAYOUTDEL", Array.Empty<string>(), "Tệp", "Xoá layout đang mở (không xoá layout cuối)"),
        new("HATCHPATTERNS", Array.Empty<string>(), "Chèn", "Liệt kê các mẫu tô có sẵn"),
        new("PATLOAD", new[] { "HATCHPATLOAD" }, "Chèn", "Nạp mẫu tô từ file .pat: PATLOAD duong/dan/acad.pat"),
        new("ATTSYNC", Array.Empty<string>(), "Block", "Đồng bộ attribute của mọi insert theo định nghĩa block"),
        new("PLOTSTYLE", Array.Empty<string>(), "Tệp", "Nạp bảng kiểu in theo lớp: PLOTSTYLE samples/kieu-in.txt"),
        new("WBLOCK", new[] { "WB" }, "Tệp", "Xuất lựa chọn ra tệp DWG riêng: WBLOCK duong/dan/ra.dwg"),
        new("IMPORT", Array.Empty<string>(), "Block", "Nạp một DWG khác thành block: IMPORT duong/dan/tep.dwg [tên]"),
        new("LAYER", new[] { "LA" }, "Khác", "Xem danh sách layer; LA <tên> đặt layer hiện hành"),
        new("LTSCALE", new[] { "LTS" }, "Xem", "Tỷ lệ kiểu nét: LTSCALE 2"),
        new("PEDIT", new[] { "PE" }, "Sửa", "Sửa polyline: PE close | PE open | PE join"),
        new("SCRIPT", new[] { "SCR" }, "Tệp", "Chạy tệp script .scr: SCRIPT duong/dan/tep.scr"),
        new("INFO", Array.Empty<string>(), "Khác", "Thống kê bản vẽ: số đối tượng theo loại, layer, vùng"),
        new("LIST", new[] { "LS" }, "Khác", "Mô tả chi tiết đối tượng đang chọn (loại, lớp, màu, hình học)"),
        new("ATTOUT", Array.Empty<string>(), "Block", "Xuất bảng attribute của mọi block ra CSV: ATTOUT thong-ke.csv"),
        new("TEXTSTYLE", new[] { "FONT" }, "Chú thích", "Đổi font chữ hiển thị: TEXTSTYLE Times New Roman"),
        new("TEXTANGLE", new[] { "TORIENT" }, "Chú thích", "Xoay chữ đang chọn tại chỗ: TEXTANGLE 45"),
        new("TABLECELL", Array.Empty<string>(), "Chèn", "Đặt chữ vào ô bảng: TABLECELL hàng,cột,nội dung"),
        new("OVERKILL", Array.Empty<string>(), "Sửa", "Xoá đối tượng trùng hình học (giữ bản đầu)"),
        new("OVERKILLMERGE", new[] { "OVM" }, "Sửa", "Gộp các đoạn thẳng chồng/nối tiếp trên cùng đường"),
        new("LINETYPES", Array.Empty<string>(), "Khác", "Liệt kê kiểu nét của bản vẽ"),
        new("LINETYPELOAD", Array.Empty<string>(), "Khác", "Nạp các kiểu nét chuẩn (DASHED, HIDDEN, CENTER…)"),
        new("AUDIT", Array.Empty<string>(), "Tệp", "Rà bản vẽ tìm lỗi dữ liệu"),
        new("PURGE", Array.Empty<string>(), "Tệp", "Dọn layer/block/kiểu nét không dùng"),
        new("DIAG", new[] { "DIAGNOSTICS" }, "Khác", "In chẩn đoán: lỗi gần nhất, file log crash"),
        new("AUTOSAVE", Array.Empty<string>(), "Tệp", "Ghi autosave ngay và báo trạng thái"),
        new("HELP", new[] { "?" }, "Khác", "Danh sách lệnh"),
    };

    /// <summary>Các cặp (alias, lệnh) bị trùng — phải rỗng.</summary>
    public static List<(string Alias, string First, string Second)> FindDuplicates()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<(string, string, string)>();

        foreach (var command in All)
        {
            foreach (var name in command.All)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name == command.Name && command.Name == "?") continue;

                if (seen.TryGetValue(name, out var owner))
                {
                    duplicates.Add((name, owner, command.Name));
                    continue;
                }

                seen[name] = command.Name;
            }
        }

        return duplicates;
    }

    /// <summary>Tìm lệnh theo tên gọi (không phân biệt hoa thường).</summary>
    /// <summary>
    /// Gợi ý lệnh theo phần đầu người dùng gõ: ưu tiên khớp tên lệnh, rồi tới alias.
    /// Trả về null nếu không có gì khớp (hoặc chuỗi rỗng).
    /// </summary>
    public static string? Suggest(string text)
    {
        var typed = (text ?? string.Empty).Trim().ToUpperInvariant();

        if (typed.Length == 0) return null;

        // Đã gõ đúng tên một lệnh → không cần gợi ý nữa
        foreach (var command in All)
        {
            if (command.Name.Equals(typed, StringComparison.OrdinalIgnoreCase)) return null;
        }

        foreach (var command in All)
        {
            if (command.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                return command.Name;
        }

        foreach (var command in All)
        {
            foreach (var alias in command.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;

                if (alias.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    return command.Name;
            }
        }

        return null;
    }

    public static CommandInfo? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var command in All)
            foreach (var candidate in command.All)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return command;

        return null;
    }

    /// <summary>Danh sách lệnh theo nhóm, để in HELP.</summary>
    public static IReadOnlyList<(string Category, List<CommandInfo> Commands)> ByCategory()
    {
        return All
            .GroupBy(c => c.Category)
            .Select(g => (g.Key, g.ToList()))
            .ToList();
    }
}
