using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;

namespace OpenCad.Core;

/// <summary>
/// Vòng đời của một bản vẽ: mở, giữ trạng thái "đã sửa", lưu.
/// Tách khỏi UI để có thể test headless và tái sử dụng cho CLI/service.
/// </summary>
public sealed class CadWorkspace
{
    public CadDocument Document { get; }
    public string? FilePath { get; private set; }
    public bool IsModified { get; private set; }
    public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();

    public string DisplayName =>
        FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    private CadWorkspace(CadDocument document, string? path)
    {
        Document = document;
        FilePath = path;
    }

    public static CadWorkspace New(ACadVersion version = ACadVersion.AC1032)
    {
        var workspace = new CadWorkspace(new CadDocument(version), null);

        // Bản vẽ mới phải có sẵn các kiểu nét chuẩn (DASHED, HIDDEN, CENTER…),
        // nếu không combo kiểu nét chỉ có Continuous và người dùng không đặt được nét đứt.
        try { OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(workspace.Document); }
        catch { }

        return workspace;
    }

    public static CadWorkspace Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file.", path);

        var warnings = new List<string>();
        NotificationEventHandler handler = (_, e) =>
        {
            try { warnings.Add(e.Message); } catch { }
        };

        var doc = IsDxf(path)
            ? DxfReader.Read(path, handler)
            : DwgReader.Read(path, handler);

        return new CadWorkspace(doc, path) { LastWarnings = warnings };
    }

    /// <summary>
    /// Đường dẫn file autosave cho bản vẽ này. Không phụ thuộc việc đã lưu lần nào chưa:
    /// bản chưa có tên dùng "untitled".
    /// </summary>
    public string AutosavePath
    {
        get
        {
            var folder = Path.Combine(Path.GetTempPath(), "OpenCAD", "autosave");
            Directory.CreateDirectory(folder);

            var name = FilePath is null
                ? "untitled"
                : Path.GetFileNameWithoutExtension(FilePath);

            // Băm cả đường dẫn để hai bản vẽ trùng tên ở thư mục khác nhau không đè nhau
            var key = (FilePath ?? "untitled").ToLowerInvariant();

            var hash = 17;

            foreach (var c in key) hash = hash * 31 + c;

            var suffix = (uint)hash % 100000;

            return Path.Combine(folder, $"{name}-{suffix}.dwg.autosave");
        }
    }

    /// <summary>Ghi bản autosave (DWG). Trả về đường dẫn, hoặc null nếu không ghi được.</summary>
    public string? WriteAutosave()
    {
        try
        {
            var path = AutosavePath;

            // Không dùng Save(): autosave không được đổi FilePath hay xoá cờ "chưa lưu"
            WriteTo(path, track: false);

            LastAutosave = DateTime.Now;

            return path;
        }
        catch { return null; }
    }

    /// <summary>Thời điểm autosave gần nhất.</summary>
    public DateTime? LastAutosave { get; private set; }

    /// <summary>Danh sách file autosave còn trên đĩa (để phục hồi sau crash).</summary>
    public static List<string> FindAutosaves()
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "OpenCAD", "autosave");

            if (!Directory.Exists(folder)) return new List<string>();

            return Directory.GetFiles(folder, "*.autosave")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Xoá file autosave của bản vẽ này (gọi sau khi người dùng lưu thật).</summary>
    public void ClearAutosave()
    {
        try
        {
            var path = AutosavePath;

            if (File.Exists(path)) File.Delete(path);

            LastAutosave = null;
        }
        catch { }
    }

    public void Save() => Save(FilePath ?? throw new InvalidOperationException(
        "Bản vẽ chưa có đường dẫn, hãy dùng SaveAs."));

    /// <summary>
    /// Các loại entity sẽ **mất khi lưu DWG** vì bộ ghi của thư viện không hỗ trợ.
    /// Đã kiểm chứng bằng round-trip: MultiLeader 1 → 0.
    /// Trả về danh sách (tên loại, số lượng) để UI cảnh báo trước khi ghi đè file.
    /// </summary>
    public IReadOnlyList<(string Type, int Count)> LossySaveRisks()
    {
        var risks = new List<(string, int)>();

        try
        {
            // MULTILEADER: từ ACadSharp 3.8.30 entity sống sót qua DWG nhưng MẤT CHỮ ghi chú.
            // Vì vậy vẫn cảnh báo và vẫn chuyển sang Leader + MText để giữ nội dung.
            var multiLeaders = Document.Entities
                .OfType<ACadSharp.Entities.MultiLeader>()
                .Count(m => !string.IsNullOrWhiteSpace(m.ContextData?.TextLabel));

            if (multiLeaders > 0) risks.Add(("MultiLeader (mất chữ)", multiLeaders));

            // TABLE: vẫn mất (bộ đọc báo "Could not read ACAD_TABLE") — trừ bảng 1×1.
            var tables = Document.Entities.Count(e => e is ACadSharp.Entities.TableEntity);

            if (tables > 0) risks.Add(("Table", tables));

        }
        catch { }

        return risks;
    }

    /// <summary>
    /// Chuyển các entity mà DWG không lưu được sang dạng tương đương lưu được.
    /// MultiLeader → Leader (đường dẫn + mũi tên) + MText (nội dung chữ).
    /// Trả về số entity đã chuyển.
    /// </summary>
    public int ConvertUnsupportedForSave()
    {
        var converted = 0;

        try
        {
            // MULTILEADER → Leader + MText (giữ được cả đường dẫn lẫn chữ khi lưu DWG)
            var victims = Document.Entities.OfType<ACadSharp.Entities.MultiLeader>().ToList();

            foreach (var multiLeader in victims)
            {
                var context = multiLeader.ContextData;

                if (context is null) continue;

                var label = (context.TextLabel ?? string.Empty).Replace("\\P", "\n").Trim();

                if (label.Length == 0) continue;

                // Đường dẫn: lấy nhánh đầu tiên có điểm
                var points = context.LeaderRoots
                    .SelectMany(r => r.Lines)
                    .FirstOrDefault(l => l.Points.Count >= 2)?.Points;

                if (points is not null)
                {
                    var leader = new Leader
                    {
                        Layer = multiLeader.Layer,
                        ArrowHeadEnabled = true,
                        TextHeight = context.TextHeight > 0 ? context.TextHeight : 100,
                    };

                    foreach (var point in points) leader.Vertices.Add(point);

                    Document.Entities.Add(leader);
                }

                var location = context.TextLocation;
                var height = context.TextHeight > 0 ? context.TextHeight : 100;

                Document.Entities.Add(new MText
                {
                    InsertPoint = new CSMath.XYZ(location.X, location.Y, location.Z),
                    Height = height,
                    Value = label,
                    Layer = multiLeader.Layer,
                });

                Document.Entities.Remove(multiLeader);
                converted++;
            }

            // TABLE: chuyển thành lưới đường + chữ để không mất hình khi lưu DWG
            var tables = Document.Entities.OfType<ACadSharp.Entities.TableEntity>().ToList();

            foreach (var table in tables)
            {
                var origin = new CSMath.XYZ(table.InsertPoint.X, table.InsertPoint.Y, table.InsertPoint.Z);

                var rows = Math.Max(1, (int)table.RowCount);
                var cols = Math.Max(1, (int)table.ColumnCount);

                var rowHeight = table.RowSpacing > 1e-6 ? table.RowSpacing : 300;
                var colWidth = table.ColumnSpacing > 1e-6 ? table.ColumnSpacing : 1200;

                var width = cols * colWidth;
                var height = rows * rowHeight;

                // Lưới kẻ: ngang + dọc
                for (var r = 0; r <= rows; r++)
                {
                    var y = origin.Y - r * rowHeight;

                    Document.Entities.Add(new Line
                    {
                        StartPoint = new CSMath.XYZ(origin.X, y, origin.Z),
                        EndPoint = new CSMath.XYZ(origin.X + width, y, origin.Z),
                        Layer = table.Layer,
                    });
                }

                for (var c = 0; c <= cols; c++)
                {
                    var x = origin.X + c * colWidth;

                    Document.Entities.Add(new Line
                    {
                        StartPoint = new CSMath.XYZ(x, origin.Y, origin.Z),
                        EndPoint = new CSMath.XYZ(x, origin.Y - height, origin.Z),
                        Layer = table.Layer,
                    });
                }

                Document.Entities.Remove(table);
                converted++;
            }
        }
        catch { }

        return converted;
    }

    /// <summary>
    /// Lưu an toàn: ghi ra file tạm rồi mới thay thế file thật, giữ bản cũ thành .bak.
    /// Nếu app chết giữa lúc ghi thì file gốc vẫn nguyên vẹn.
    /// </summary>
    public void Save(string path)
    {
        var temp = path + ".tmp";

        try
        {
            WriteTo(temp, track: false);

            // Giữ bản cũ (chỉ một cấp, giống AutoCAD)
            if (File.Exists(path))
            {
                try { File.Copy(path, path + ".bak", overwrite: true); }
                catch { }
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { }

            throw;
        }

        FilePath = path;
        IsModified = false;
    }

    /// <summary>Đường dẫn bản sao lưu của lần lưu trước (nếu có).</summary>
    public string? BackupPath => FilePath is null ? null : FilePath + ".bak";

    /// <summary>
    /// Ghi bản vẽ ra file. <paramref name="track"/> = true thì coi đây là lưu thật
    /// (đổi FilePath, xoá cờ chưa lưu); autosave dùng track = false.
    /// </summary>
    private void WriteTo(string path, bool track)
    {
        var warnings = new List<string>();
        NotificationEventHandler handler = (_, e) =>
        {
            try { warnings.Add(e.Message); } catch { }
        };

        if (IsDxf(path))
            DxfWriter.Write(path, Document, false, new DxfWriterConfiguration(), handler);
        else
            DwgWriter.Write(path, Document, new DwgWriterConfiguration(), handler);

        LastWarnings = warnings;

        if (!track) return;

        FilePath = path;
        IsModified = false;
    }

    public Entity Add(Entity entity)
    {
        if (entity.Layer is null)
        {
            var zero = FindLayer("0");
            if (zero is not null) entity.Layer = zero;
        }

        Document.Entities.Add(entity);
        IsModified = true;
        return entity;
    }

    public bool Remove(Entity entity)
    {
        var removed = Document.Entities.Remove(entity);
        if (removed) IsModified = true;
        return removed;
    }

    public void MarkModified() => IsModified = true;

    /// <summary>Đặt lại cờ "đã sửa" (dùng khi undo về đúng điểm đã lưu).</summary>
    public void SetModified(bool value) => IsModified = value;

    public Layer? FindLayer(string name)
    {
        try
        {
            foreach (var layer in Document.Layers)
                if (string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase))
                    return layer;
        }
        catch { }
        return null;
    }

    public IEnumerable<string> LayerNames()
    {
        try { return Document.Layers.Select(l => l.Name).OrderBy(n => n).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Đoán định dạng theo NỘI DUNG trước, rồi mới tới đuôi file.
    /// Nhờ vậy mở được file .bak / .autosave / tên bất kỳ.
    /// </summary>
    public static bool IsDxf(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                Span<byte> head = stackalloc byte[6];
                var read = stream.Read(head);

                // DWG luôn bắt đầu bằng "AC10xx"
                if (read >= 6 && head[0] == (byte)'A' && head[1] == (byte)'C' &&
                    head[2] == (byte)'1' && head[3] == (byte)'0')
                    return false;

                // DXF là văn bản
                if (read > 0) return true;
            }
        }
        catch { }

        return string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase);
    }
}
