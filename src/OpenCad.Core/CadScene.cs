using ACadSharp;
using ACadSharp.Entities;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core;

/// <summary>
/// Ảnh chụp hình học của bản vẽ: mỗi entity kèm danh sách primitive đã làm phẳng.
/// Dùng chung cho render, hit-test, zoom-extents và tính hộp bao.
/// </summary>
public sealed class CadScene
{
    public sealed record Entry(Entity Entity, List<Prim> Prims, RectD Bounds);

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;
    public RectD Bounds { get; private set; } = RectD.Empty;
    public int PrimitiveCount { get; private set; }
    public int SkippedCount { get; private set; }

    /// <summary>Đếm entity không vẽ được theo loại — để cảnh báo thay vì mất dữ liệu âm thầm.</summary>
    public IReadOnlyDictionary<string, int> SkippedTypes => _skippedTypes;

    private readonly Dictionary<string, int> _skippedTypes = new(StringComparer.Ordinal);

    public void Build(CadDocument document) => Build(document.Entities);

    /// <summary>Dựng scene, bỏ qua entity không hiển thị (layer tắt/đóng băng).</summary>
    public void Build(CadDocument document, Func<Entity, bool>? visible)
        => Build(document.Entities, visible);

    /// <summary>Dựng scene từ một tập entity bất kỳ (dùng cho paper space).</summary>
    public void Build(IEnumerable<Entity> entities) => Build(entities, null);

    /// <summary>
    /// Dựng scene từ tập entity. Entity không hiển thị bị bỏ hẳn — nhờ vậy chúng
    /// cũng không click/move/xoá được, đúng như AutoCAD.
    /// </summary>
    public void Build(IEnumerable<Entity> entities, Func<Entity, bool>? visible)
    {
        _entries.Clear();
        _grid = null;
        _skippedTypes.Clear();
        var bounds = RectD.Empty;
        var prims = 0;
        var skipped = 0;

        foreach (var entity in entities)
        {
            if (visible is not null && !visible(entity)) continue;

            List<Prim> list;
            try { list = FlattenOne(entity); }
            catch { list = new List<Prim>(); }

            if (list.Count == 0)
            {
                skipped++;

                var typeName = entity.GetType().Name;
                _skippedTypes[typeName] = _skippedTypes.TryGetValue(typeName, out var seen)
                    ? seen + 1
                    : 1;
            }

            var entityBounds = RectD.Empty;
            foreach (var p in list) entityBounds = entityBounds.Union(p.Bounds());

            _entries.Add(new Entry(entity, list, entityBounds));
            bounds = bounds.Union(entityBounds);
            prims += list.Count;
        }

        Bounds = bounds;
        PrimitiveCount = prims;
        SkippedCount = skipped;
    }

    private static List<Prim> FlattenOne(Entity entity)
    {
        var list = new List<Prim>(4);
        EntityFlattener.Flatten(entity, list,
            Affine2.Identity, Colors.Rgb.White, 0);
        return list;
    }

    /// <summary>Tính lại hình học cho một entity sau khi sửa (nhanh hơn Build toàn bộ).</summary>
    public void Rebuild(Entity entity)
    {
        _grid = null;
        RebuildCore(entity);
    }

    private void RebuildCore(Entity entity)
    {
        var index = _entries.FindIndex(e => ReferenceEquals(e.Entity, entity));
        if (index < 0) return;

        List<Prim> list;
        try { list = FlattenOne(entity); }
        catch { list = new List<Prim>(); }

        var b = RectD.Empty;
        foreach (var p in list) b = b.Union(p.Bounds());

        _entries[index] = new Entry(entity, list, b);
        RecomputeBounds();
    }

    public void RecomputeBounds()
    {
        var bounds = RectD.Empty;
        foreach (var e in _entries) bounds = bounds.Union(e.Bounds);
        Bounds = bounds;
        PrimitiveCount = _entries.Sum(e => e.Prims.Count);
        SkippedCount = _entries.Count(e => e.Prims.Count == 0);
    }

    /// <summary>Entity gần nhất trong bán kính <paramref name="tolerance"/> (đơn vị world).</summary>
    // ---------------------------------------------------------- CHỈ MỤC LƯỚI
    // Bản vẽ lớn (20k entity) mà quét tuyến tính mỗi lần click thì chậm dần theo
    // kích thước bản vẽ. Lưới đều giúp chỉ xét các entity gần con trỏ.

    private Dictionary<long, List<Entry>>? _grid;
    private List<Entry>? _gridLarge;
    private double _gridCell = 1;

    private void BuildGrid()
    {
        _grid = new Dictionary<long, List<Entry>>();
        _gridLarge = new List<Entry>();

        var bounds = Bounds;
        if (bounds.IsEmpty || _entries.Count == 0)
        {
            _gridCell = 1;
            return;
        }

        // Chọn cạnh ô sao cho trung bình ~4 entity mỗi ô
        var area = Math.Max(1e-6, bounds.Width * bounds.Height);
        _gridCell = Math.Max(1e-6, Math.Sqrt(area / Math.Max(1, _entries.Count / 4.0)));

        foreach (var entry in _entries)
        {
            if (entry.Prims.Count == 0 || entry.Bounds.IsEmpty) continue;

            var x0 = (int)Math.Floor(entry.Bounds.MinX / _gridCell);
            var x1 = (int)Math.Floor(entry.Bounds.MaxX / _gridCell);
            var y0 = (int)Math.Floor(entry.Bounds.MinY / _gridCell);
            var y1 = (int)Math.Floor(entry.Bounds.MaxY / _gridCell);

            // Entity trải quá nhiều ô thì luôn kiểm tra
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 64)
            {
                _gridLarge.Add(entry);
                continue;
            }

            for (var gx = x0; gx <= x1; gx++)
            for (var gy = y0; gy <= y1; gy++)
            {
                var key = ((long)gx << 32) ^ (uint)gy;

                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<Entry>(4);
                    _grid[key] = list;
                }

                list.Add(entry);
            }
        }
    }

    private static long CellKey(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;

    public Entry? HitTest(Pt world, double tolerance)
    {
        Entry? best = null;
        var bestDist = tolerance;

        if (_grid is null) BuildGrid();

        if (_grid is null || _grid.Count == 0)
        {
            foreach (var entry in _entries) Consider(entry, world, tolerance, ref best, ref bestDist);
            return best;
        }

        var cx0 = (int)Math.Floor((world.X - tolerance) / _gridCell);
        var cx1 = (int)Math.Floor((world.X + tolerance) / _gridCell);
        var cy0 = (int)Math.Floor((world.Y - tolerance) / _gridCell);
        var cy1 = (int)Math.Floor((world.Y + tolerance) / _gridCell);

        var visited = new HashSet<Entry>();

        for (var gx = cx0; gx <= cx1; gx++)
        for (var gy = cy0; gy <= cy1; gy++)
        {
            if (!_grid.TryGetValue(CellKey(gx, gy), out var list)) continue;

            foreach (var entry in list)
            {
                if (!visited.Add(entry)) continue;
                Consider(entry, world, tolerance, ref best, ref bestDist);
            }
        }

        if (_gridLarge is not null)
            foreach (var entry in _gridLarge)
                Consider(entry, world, tolerance, ref best, ref bestDist);

        return best;
    }

    private static void Consider(Entry entry, Pt world, double tolerance,
                                 ref Entry? best, ref double bestDist)
    {
        if (entry.Prims.Count == 0) return;

        // Lọc nhanh bằng hộp bao
        if (!entry.Bounds.IsEmpty && !entry.Bounds.Inflate(tolerance).Contains(world))
            return;

        var d = DistanceToPrims(entry.Prims, world);

        if (d >= bestDist) return;

        bestDist = d;
        best = entry;
    }

    public IEnumerable<Entry> HitTestAll(Pt world, double tolerance)
    {
        var result = new List<Entry>();
        foreach (var entry in _entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (DistanceToPrims(entry.Prims, world) <= tolerance) result.Add(entry);
        }
        return result;
    }

    /// <summary>
    /// Chọn theo khung. <paramref name="crossing"/> = true thì lấy mọi entity
    /// giao với khung (quét từ phải sang trái), false thì chỉ lấy entity nằm trọn trong khung.
    /// </summary>
    public List<Entity> SelectInRect(RectD rect, bool crossing)
    {
        var result = new List<Entity>();

        foreach (var entry in _entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (entry.Bounds.IsEmpty) continue;

            if (!crossing)
            {
                // Window: hộp bao phải nằm trọn trong khung
                if (entry.Bounds.MinX >= rect.MinX && entry.Bounds.MaxX <= rect.MaxX &&
                    entry.Bounds.MinY >= rect.MinY && entry.Bounds.MaxY <= rect.MaxY)
                    result.Add(entry.Entity);
                continue;
            }

            // Crossing: hộp bao giao khung, hoặc một đoạn bất kỳ cắt khung
            if (!entry.Bounds.Intersects(rect)) continue;

            if (entry.Bounds.MinX >= rect.MinX && entry.Bounds.MaxX <= rect.MaxX &&
                entry.Bounds.MinY >= rect.MinY && entry.Bounds.MaxY <= rect.MaxY)
            {
                result.Add(entry.Entity);
                continue;
            }

            if (entry.Prims.Any(p => PrimCrossesRect(p, rect)))
                result.Add(entry.Entity);
        }

        return result;
    }

    private static bool PrimCrossesRect(Prim prim, RectD rect)
    {
        if (prim.Kind == PrimKind.Text)
            return prim.Bounds().Intersects(rect);

        if (prim.Kind == PrimKind.Point)
            return rect.Contains(prim.Position);

        if (prim.Kind == PrimKind.Hatch)
            return prim.Bounds().Intersects(rect);

        var pts = prim.Points;
        for (var i = 0; i < pts.Length; i++)
        {
            if (rect.Contains(pts[i])) return true;
            if (i + 1 < pts.Length && SegmentCrossesRect(pts[i], pts[i + 1], rect)) return true;
        }

        if (prim.Closed && pts.Length > 2 && SegmentCrossesRect(pts[^1], pts[0], rect)) return true;

        return false;
    }

    private static bool SegmentCrossesRect(Pt a, Pt b, RectD r)
    {
        var seg = new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (!seg.Intersects(r)) return false;

        var edges = new[]
        {
            (new Pt(r.MinX, r.MinY), new Pt(r.MaxX, r.MinY)),
            (new Pt(r.MaxX, r.MinY), new Pt(r.MaxX, r.MaxY)),
            (new Pt(r.MaxX, r.MaxY), new Pt(r.MinX, r.MaxY)),
            (new Pt(r.MinX, r.MaxY), new Pt(r.MinX, r.MinY)),
        };

        foreach (var (e1, e2) in edges)
            if (SegmentsIntersect(a, b, e1, e2)) return true;

        return false;
    }

    private static bool SegmentsIntersect(Pt a1, Pt a2, Pt b1, Pt b2)
    {
        static double Orient(Pt p, Pt q, Pt r) => Pt.Cross(q - p, r - p);

        var d1 = Orient(b1, b2, a1);
        var d2 = Orient(b1, b2, a2);
        var d3 = Orient(a1, a2, b1);
        var d4 = Orient(a1, a2, b2);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double DistanceToPrims(List<Prim> prims, Pt p)
    {
        var best = double.PositiveInfinity;
        foreach (var prim in prims)
        {
            var d = DistanceToPrim(prim, p);
            if (d < best) best = d;
        }
        return best;
    }

    private static double DistanceToPrim(Prim prim, Pt p)
    {
        switch (prim.Kind)
        {
            case PrimKind.Point:
                return Pt.Distance(prim.Position, p);

            case PrimKind.Text:
            {
                var b = prim.Bounds();
                if (b.Contains(p)) return 0;
                // khoảng cách tới mép hộp bao
                var dx = Math.Max(0, Math.Max(b.MinX - p.X, p.X - b.MaxX));
                var dy = Math.Max(0, Math.Max(b.MinY - p.Y, p.Y - b.MaxY));
                return Math.Sqrt(dx * dx + dy * dy);
            }

            case PrimKind.Hatch:
            {
                // Chọn được cả phần bên trong vùng tô, không chỉ viền
                if (prim.ContainsPoint(p)) return 0;
                if (prim.Loops is null) return double.PositiveInfinity;

                var best = double.PositiveInfinity;
                foreach (var loop in prim.Loops)
                {
                    for (var i = 0; i < loop.Length; i++)
                        best = Math.Min(best, DistanceToSegment(p, loop[i], loop[(i + 1) % loop.Length]));
                }
                return best;
            }

            default:
            {
                var pts = prim.Points;
                if (pts.Length == 0) return double.PositiveInfinity;
                if (pts.Length == 1) return Pt.Distance(pts[0], p);

                var best = double.PositiveInfinity;
                for (var i = 0; i < pts.Length - 1; i++)
                    best = Math.Min(best, DistanceToSegment(p, pts[i], pts[i + 1]));

                if (prim.Closed && pts.Length > 2)
                    best = Math.Min(best, DistanceToSegment(p, pts[^1], pts[0]));

                return best;
            }
        }
    }

    public static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared;
        if (lenSq < 1e-18) return Pt.Distance(p, a);

        var t = Math.Clamp(Pt.Dot(p - a, ab) / lenSq, 0, 1);
        return Pt.Distance(p, a + ab * t);
    }
}
