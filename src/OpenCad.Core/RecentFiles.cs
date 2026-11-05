using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCad.Core;

/// <summary>
/// Danh sách tệp mở gần đây, lưu giữa các lần chạy trong %APPDATA%\OpenCAD\recent.txt.
/// Giữ tối đa 10 mục, không trùng, bỏ mục đã bị xoá khỏi đĩa.
/// </summary>
public static class RecentFiles
{
    /// <summary>Số mục tối đa giữ lại.</summary>
    public const int Max = 10;

    /// <summary>Đường dẫn file lưu danh sách (đổi được để kiểm thử).</summary>
    public static string StorePath { get; set; } = DefaultStorePath();

    private static string DefaultStorePath()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenCAD");

            return Path.Combine(folder, "recent.txt");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "opencad-recent.txt");
        }
    }

    /// <summary>Đọc danh sách; bỏ qua mục không còn tồn tại trên đĩa.</summary>
    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<string>();

            return File.ReadAllLines(StorePath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && File.Exists(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Max)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Thêm một tệp vào đầu danh sách (không trùng), ghi xuống đĩa. Trả về danh sách mới.</summary>
    public static List<string> Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Load();

        var full = SafeFullPath(path);

        if (full is null) return Load();

        var items = Load()
            .Where(item => !string.Equals(item, full, StringComparison.OrdinalIgnoreCase))
            .ToList();

        items.Insert(0, full);

        if (items.Count > Max) items = items.Take(Max).ToList();

        Save(items);

        return items;
    }

    /// <summary>Ghi danh sách xuống đĩa (bỏ mục không còn tồn tại).</summary>
    public static void Save(IEnumerable<string> items)
    {
        try
        {
            var cleaned = items
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Take(Max)
                .ToList();

            var folder = Path.GetDirectoryName(StorePath);

            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllLines(StorePath, cleaned);
        }
        catch { }
    }

    /// <summary>Xoá danh sách (dùng cho kiểm thử và cho menu "Xoá danh sách").</summary>
    public static void Clear()
    {
        try { if (File.Exists(StorePath)) File.Delete(StorePath); }
        catch { }
    }

    private static string? SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }
}
