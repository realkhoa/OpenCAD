using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCad.Core.Styles;

/// <summary>
/// Ánh xạ tên font hiển thị → tệp .ttf trên máy, và đọc/ghi font của bản vẽ
/// thông qua TextStyle "Standard" (để font đi theo tệp DWG).
/// </summary>
public static class FontLibrary
{
    /// <summary>Tên font mặc định của OpenCAD.</summary>
    public const string DefaultFont = "Segoe UI";

    // Các font hay dùng, kèm tên tệp trên Windows
    private static readonly Dictionary<string, string> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Segoe UI"] = "segoeui.ttf",
            ["Arial"] = "arial.ttf",
            ["Times New Roman"] = "times.ttf",
            ["Tahoma"] = "tahoma.ttf",
            ["Verdana"] = "verdana.ttf",
            ["Calibri"] = "calibri.ttf",
            ["Consolas"] = "consola.ttf",
            ["Courier New"] = "cour.ttf",
            ["Microsoft Sans Serif"] = "micross.ttf",
            ["Trebuchet MS"] = "trebuc.ttf",
            ["Roboto Slab"] = "robotoslab-regular.ttf",
        };

    /// <summary>Thư mục font của Windows.</summary>
    private static string FontFolder
    {
        get
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            }
            catch { return "C:\\Windows\\Fonts"; }
        }
    }

    /// <summary>Tìm tệp .ttf của một font. Trả về null nếu không thấy.</summary>
    public static string? FileFor(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return null;

        var name = fontName.Trim();

        // Người dùng có thể truyền thẳng đường dẫn tệp
        if (name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && File.Exists(name))
            return name;

        var folder = FontFolder;

        if (!Directory.Exists(folder)) return null;

        if (Known.TryGetValue(name, out var fileName))
        {
            var full = Path.Combine(folder, fileName);

            if (File.Exists(full)) return full;
        }

        // Thử vài biến thể tên tệp: "Times New Roman" → timesnewroman.ttf / times.ttf
        var compact = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        try
        {
            foreach (var candidate in new[]
                     {
                         compact + ".ttf",
                         compact.Replace("new", "") + ".ttf",
                         compact + "bd.ttf",
                     })
            {
                var full = Path.Combine(folder, candidate);

                if (File.Exists(full)) return full;
            }

            // Cuối cùng: tìm tệp có tên gần giống
            return Directory.GetFiles(folder, "*.ttf")
                .FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).ToLowerInvariant().StartsWith(compact));
        }
        catch { return null; }
    }

    /// <summary>Đọc font đã lưu trong bản vẽ (TextStyle "Standard"). Rỗng nếu chưa có.</summary>
    public static string GetDocumentFont(ACadSharp.CadDocument document)
    {
        try
        {
            var style = document.TextStyles.FirstOrDefault(
                s => s.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase));

            var file = style?.Filename;

            if (string.IsNullOrWhiteSpace(file)) return string.Empty;

            // Lưu tên font trong Filename; nếu là đường dẫn thì lấy tên tệp
            var name = Path.GetFileNameWithoutExtension(file);

            // Ánh xạ ngược tên tệp → tên font hiển thị
            foreach (var pair in Known)
            {
                if (Path.GetFileNameWithoutExtension(pair.Value)
                    .Equals(name, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            }

            return name;
        }
        catch { return string.Empty; }
    }

    /// <summary>Ghi tên font vào bản vẽ để lần sau mở lên vẫn giữ.</summary>
    public static bool SetDocumentFont(ACadSharp.CadDocument document, string fontName)
    {
        if (document is null || string.IsNullOrWhiteSpace(fontName)) return false;

        try
        {
            var style = document.TextStyles.FirstOrDefault(
                s => s.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase));

            if (style is null)
            {
                style = new ACadSharp.Tables.TextStyle("Standard");
                document.TextStyles.Add(style);
            }

            style.Filename = fontName.Trim();

            return true;
        }
        catch { return false; }
    }
}
