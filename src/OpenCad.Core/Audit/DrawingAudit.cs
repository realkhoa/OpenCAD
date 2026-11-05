using ACadSharp;
using ACadSharp.Entities;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Audit;

/// <summary>Một vấn đề tìm thấy khi rà bản vẽ.</summary>
public sealed record AuditFinding(string Severity, string Code, string Message, Entity? Entity)
{
    public override string ToString() => $"[{Severity}] {Message}";
}

/// <summary>
/// Rà bản vẽ tìm lỗi dữ liệu — tương tự lệnh AUDIT của AutoCAD.
/// Chỉ đọc, không sửa gì.
/// </summary>
public static class DrawingAudit
{
    /// <summary>Ngưỡng coi là "không có kích thước" (đơn vị bản vẽ).</summary>
    public const double TinyTolerance = 1e-6;

    public static List<AuditFinding> Run(CadDocument document)
    {
        var findings = new List<AuditFinding>();

        if (document is null) return findings;

        var entities = document.Entities.ToList();

        foreach (var entity in entities)
        {
            try { CheckEntity(entity, findings); }
            catch (Exception ex)
            {
                findings.Add(new AuditFinding("Lỗi", "EXCEPTION",
                    $"{entity.GetType().Name}: không đọc được hình học ({ex.GetType().Name})", entity));
            }
        }

        CheckDuplicates(entities, findings);
        CheckBlocks(document, findings);
        CheckInserts(document, findings);

        return findings;
    }

    private static void CheckEntity(Entity entity, List<AuditFinding> findings)
    {
        switch (entity)
        {
            case Line line:
            {
                var length = Math.Sqrt(
                    Math.Pow(line.EndPoint.X - line.StartPoint.X, 2) +
                    Math.Pow(line.EndPoint.Y - line.StartPoint.Y, 2));

                if (length < TinyTolerance)
                    findings.Add(new AuditFinding("Cảnh báo", "ZERO_LINE",
                        $"Đoạn thẳng dài 0 tại ({line.StartPoint.X:0.##}, {line.StartPoint.Y:0.##})", entity));

                break;
            }

            case Circle circle when circle.Radius < TinyTolerance:
                findings.Add(new AuditFinding("Cảnh báo", "ZERO_CIRCLE",
                    $"Đường tròn bán kính 0 tại ({circle.Center.X:0.##}, {circle.Center.Y:0.##})", entity));
                break;

            case Arc arc when arc.Radius < TinyTolerance:
                findings.Add(new AuditFinding("Cảnh báo", "ZERO_ARC",
                    $"Cung bán kính 0 tại ({arc.Center.X:0.##}, {arc.Center.Y:0.##})", entity));
                break;

            case TextEntity text when string.IsNullOrWhiteSpace(text.Value):
                findings.Add(new AuditFinding("Nhẹ", "EMPTY_TEXT",
                    $"Chữ rỗng tại ({text.InsertPoint.X:0.##}, {text.InsertPoint.Y:0.##})", entity));
                break;

            case TextEntity text when text.Height < TinyTolerance:
                findings.Add(new AuditFinding("Cảnh báo", "ZERO_TEXT_HEIGHT",
                    $"Chữ cao 0: \"{text.Value}\"", entity));
                break;

            case Hatch hatch when hatch.Paths is null || hatch.Paths.Count == 0:
                findings.Add(new AuditFinding("Cảnh báo", "EMPTY_HATCH", "Mảng tô không có đường biên", entity));
                break;

            case Insert insert when insert.Block is null:
                findings.Add(new AuditFinding("Lỗi", "MISSING_BLOCK",
                    "Block tham chiếu không tồn tại", entity));
                break;
        }
    }

    /// <summary>Hai entity cùng loại và cùng hình học đã làm phẳng.</summary>
    private static void CheckDuplicates(List<Entity> entities, List<AuditFinding> findings)
    {
        var seen = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var reported = 0;

        foreach (var entity in entities)
        {
            string signature;

            try { signature = Signature(entity); }
            catch { continue; }

            if (signature.Length == 0) continue;

            if (seen.ContainsKey(signature))
            {
                var where = string.Empty;

                try
                {
                    var prims = new List<Prim>(2);
                    EntityFlattener.Flatten(entity, prims, Affine2.Identity, Colors.Rgb.White, 0);

                    if (prims.Count > 0 && prims[0].Points.Length > 0)
                    {
                        var p0 = prims[0].Points[0];
                        where = $" tại ({p0.X:0.##}, {p0.Y:0.##})";
                    }
                }
                catch { }

                findings.Add(new AuditFinding("Nhẹ", "DUPLICATE",
                    $"Trùng lặp: {entity.GetType().Name}{where} (cùng hình học với một đối tượng khác)", entity));

                if (++reported >= 50) return;
                continue;
            }

            seen[signature] = entity;
        }
    }

    internal static string Signature(Entity entity)
    {
        var prims = new List<Prim>(4);

        EntityFlattener.Flatten(entity, prims, Affine2.Identity, Colors.Rgb.White, 0);

        if (prims.Count == 0) return string.Empty;

        // Chữ ký phải bao gồm MỌI thứ quyết định hình: với Prim chữ/điểm thì `Points` RỖNG,
        // nên phải lấy Position + Height + Rotation — nếu không, hai chữ 'N' ở hai chỗ khác
        // nhau sẽ bị coi là trùng (AUDIT báo nhầm, OVERKILL xoá nhầm).
        var parts = prims.Select(p =>
        {
            string shape;

            if (p.Points.Length > 0)
            {
                shape = string.Join(",", p.Points.Select(pt =>
                    Math.Round(pt.X, 3) + "/" + Math.Round(pt.Y, 3)));
            }
            else if (p.Loops is { Length: > 0 } loops)
            {
                // Hatch: hình học nằm trong Loops (Points rỗng, Position = 0,0)
                shape = string.Join("|", loops.Select(loop =>
                    string.Join(",", loop.Select(pt =>
                        Math.Round(pt.X, 3) + "/" + Math.Round(pt.Y, 3)))));
            }
            else
            {
                shape = "at " + Math.Round(p.Position.X, 3) + "/" + Math.Round(p.Position.Y, 3)
                        + "/h" + Math.Round(p.Height, 3)
                        + "/r" + Math.Round(p.Rotation, 4);
            }

            if (p.Kind == PrimKind.Text) shape += "|t:" + p.Text;

            return p.Kind + (p.Closed ? "c" : "") + (p.Filled ? "f" : "") + "(" + shape + ")";
        });

        return entity.GetType().Name + "[" + entity.Layer?.Name + "]{" + string.Join(";", parts) + "}";
    }

    private static void CheckBlocks(CadDocument document, List<AuditFinding> findings)
    {
        foreach (var block in document.BlockRecords)
        {
            if (block.Name.StartsWith("*")) continue;

            if (block.Entities.Count() == 0)
                findings.Add(new AuditFinding("Nhẹ", "EMPTY_BLOCK",
                    $"Block rỗng: {block.Name}", null));
        }
    }

    private static void CheckInserts(CadDocument document, List<AuditFinding> findings)
    {
        var blockNames = new HashSet<string>(
            document.BlockRecords.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var insert in document.Entities.OfType<Insert>())
        {
            var name = insert.Block?.Name;

            if (name is not null && !blockNames.Contains(name))
                findings.Add(new AuditFinding("Lỗi", "MISSING_BLOCK",
                    $"Tham chiếu tới block không tồn tại: {name}", insert));
        }
    }

    /// <summary>Đếm theo mã lỗi để hiển thị tổng hợp.</summary>
    public static IReadOnlyList<(string Code, int Count)> Summary(IReadOnlyList<AuditFinding> findings)
    {
        return findings
            .GroupBy(f => f.Code)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(g => g.Item2)
            .ToList();
    }
}
