using ACadSharp.Entities;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Snapping;

public enum SnapKind
{
    None,
    Endpoint,
    Midpoint,
    Center,
    Quadrant,
    Intersection,
    Nearest,
    Grid,
}

public sealed class SnapResult
{
    public SnapKind Kind { get; init; }
    public Pt Point { get; init; }
    public Entity? Entity { get; init; }
    public double Distance { get; init; }

    public string Label => Kind switch
    {
        SnapKind.Endpoint => "Endpoint",
        SnapKind.Midpoint => "Midpoint",
        SnapKind.Center => "Center",
        SnapKind.Quadrant => "Quadrant",
        SnapKind.Intersection => "Intersection",
        SnapKind.Nearest => "Nearest",
        SnapKind.Grid => "Grid",
        _ => "",
    };
}

/// <summary>
/// Object snap (OSNAP) kiểu AutoCAD: bắt điểm endpoint / midpoint / center /
/// quadrant / intersection / nearest trên hình học đã làm phẳng của scene.
/// </summary>
public sealed class SnapEngine
{
    private const double Eps = 1e-12;

    public bool Enabled { get; set; } = true;
    public double PixelTolerance { get; set; } = 12;
    public bool GridEnabled { get; set; }
    public double GridSize { get; set; } = 100;
    public bool OrthoEnabled { get; set; }

    /// <summary>Polar tracking: bắt hướng theo bước góc (mặc định 15°).</summary>
    public bool PolarEnabled { get; set; }
    public double PolarIncrementDegrees { get; set; } = 15;

    public HashSet<SnapKind> Modes { get; } = new()
    {
        SnapKind.Endpoint, SnapKind.Midpoint, SnapKind.Center,
        SnapKind.Quadrant, SnapKind.Intersection, SnapKind.Nearest,
    };

    /// <summary>
    /// Tìm điểm snap gần con trỏ nhất. Trả về null nếu không có gì trong bán kính.
    /// </summary>
    public SnapResult? Find(CadScene scene, Pt cursor, double zoom)
    {
        if (!Enabled || zoom <= 0) return null;

        var tol = PixelTolerance / zoom;
        SnapResult? best = null;

        void Consider(SnapKind kind, Pt point, Entity? entity)
        {
            var d = Pt.Distance(point, cursor);
            if (d > tol) return;

            if (best is not null)
            {
                var bp = Priority(best.Kind);
                var kp = Priority(kind);
                if (bp < kp) return;
                if (bp == kp && best.Distance <= d) return;
            }

            best = new SnapResult { Kind = kind, Point = point, Entity = entity, Distance = d };
        }

        var segments = new List<(Pt A, Pt B, Entity Owner)>(256);

        foreach (var entry in scene.Entries)
        {
            if (entry.Prims.Count == 0) continue;
            if (!entry.Bounds.IsEmpty && !entry.Bounds.Inflate(tol).Contains(cursor)) continue;

            foreach (var prim in entry.Prims)
            {
                switch (prim.Kind)
                {
                    case PrimKind.Point:
                        if (Modes.Contains(SnapKind.Endpoint))
                            Consider(SnapKind.Endpoint, prim.Position, entry.Entity);
                        break;

                    case PrimKind.Text:
                        break;

                    default:
                        CollectFromPrim(prim, entry.Entity, cursor, tol, Consider, segments);
                        break;
                }
            }
        }

        // Giao điểm giữa các entity khác nhau
        if (Modes.Contains(SnapKind.Intersection) && segments.Count > 1)
        {
            var near = segments
                .Where(s => SegmentNearCursor(s.A, s.B, cursor, tol))
                .Take(400)
                .ToList();

            for (var i = 0; i < near.Count; i++)
            {
                for (var j = i + 1; j < near.Count; j++)
                {
                    if (ReferenceEquals(near[i].Owner, near[j].Owner)) continue;
                    if (TryIntersect(near[i].A, near[i].B, near[j].A, near[j].B, out var x))
                        Consider(SnapKind.Intersection, x, near[i].Owner);
                }
            }
        }

        if (best is null && GridEnabled && GridSize > 0)
        {
            var gx = Math.Round(cursor.X / GridSize) * GridSize;
            var gy = Math.Round(cursor.Y / GridSize) * GridSize;
            var gp = new Pt(gx, gy);
            if (Pt.Distance(gp, cursor) <= tol)
                best = new SnapResult { Kind = SnapKind.Grid, Point = gp, Distance = Pt.Distance(gp, cursor) };
        }

        return best;
    }

    private void CollectFromPrim(Prim prim, Entity owner, Pt cursor, double tol,
                                 Action<SnapKind, Pt, Entity?> consider,
                                 List<(Pt, Pt, Entity)> segments)
    {
        var pts = prim.Points;
        if (pts.Length == 0) return;

        if (prim.Center is { } center && prim.Radius > 0)
        {
            if (Modes.Contains(SnapKind.Center)) consider(SnapKind.Center, center, owner);

            if (Modes.Contains(SnapKind.Quadrant))
            {
                var r = prim.Radius;
                consider(SnapKind.Quadrant, new Pt(center.X + r, center.Y), owner);
                consider(SnapKind.Quadrant, new Pt(center.X - r, center.Y), owner);
                consider(SnapKind.Quadrant, new Pt(center.X, center.Y + r), owner);
                consider(SnapKind.Quadrant, new Pt(center.X, center.Y - r), owner);
            }
        }

        var isCurve = prim.IsCurve;

        if (!isCurve && Modes.Contains(SnapKind.Endpoint))
        {
            for (var i = 0; i < pts.Length; i++)
                consider(SnapKind.Endpoint, pts[i], owner);
        }
        else if (isCurve && Modes.Contains(SnapKind.Endpoint))
        {
            // Với đường cong, chỉ hai đầu mút là "endpoint" thật
            if (!prim.Closed)
            {
                consider(SnapKind.Endpoint, pts[0], owner);
                consider(SnapKind.Endpoint, pts[^1], owner);
            }
        }

        var limit = pts.Length - 1;
        for (var i = 0; i < limit; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];

            if (!SegmentNearCursor(a, b, cursor, tol)) continue;

            if (!isCurve && Modes.Contains(SnapKind.Midpoint))
                consider(SnapKind.Midpoint, Pt.Lerp(a, b, 0.5), owner);

            if (Modes.Contains(SnapKind.Nearest))
                consider(SnapKind.Nearest, ClosestOnSegment(cursor, a, b), owner);

            segments.Add((a, b, owner));
        }

        if (prim.Closed && pts.Length > 2)
        {
            var a = pts[^1];
            var b = pts[0];
            if (SegmentNearCursor(a, b, cursor, tol))
            {
                if (!isCurve && Modes.Contains(SnapKind.Midpoint))
                    consider(SnapKind.Midpoint, Pt.Lerp(a, b, 0.5), owner);
                if (Modes.Contains(SnapKind.Nearest))
                    consider(SnapKind.Nearest, ClosestOnSegment(cursor, a, b), owner);
                segments.Add((a, b, owner));
            }
        }
    }

    private static int Priority(SnapKind kind) => kind switch
    {
        SnapKind.Endpoint => 0,
        SnapKind.Intersection => 1,
        SnapKind.Midpoint => 2,
        SnapKind.Center => 3,
        SnapKind.Quadrant => 4,
        SnapKind.Nearest => 5,
        SnapKind.Grid => 6,
        _ => 9,
    };

    private static bool SegmentNearCursor(Pt a, Pt b, Pt cursor, double tol)
    {
        var minX = Math.Min(a.X, b.X) - tol;
        var maxX = Math.Max(a.X, b.X) + tol;
        var minY = Math.Min(a.Y, b.Y) - tol;
        var maxY = Math.Max(a.Y, b.Y) + tol;
        return cursor.X >= minX && cursor.X <= maxX && cursor.Y >= minY && cursor.Y <= maxY;
    }

    public static Pt ClosestOnSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared;
        if (lenSq < 1e-18) return a;
        var t = Math.Clamp(Pt.Dot(p - a, ab) / lenSq, 0, 1);
        return a + ab * t;
    }

    private static bool TryIntersect(Pt a1, Pt a2, Pt b1, Pt b2, out Pt result)
    {
        result = Pt.Zero;

        var r = a2 - a1;
        var s = b2 - b1;
        var denom = Pt.Cross(r, s);
        if (Math.Abs(denom) < Eps) return false;

        var qp = b1 - a1;
        var t = Pt.Cross(qp, s) / denom;
        var u = Pt.Cross(qp, r) / denom;

        if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;

        result = a1 + r * t;
        return true;
    }

    /// <summary>Kẹp điểm theo chế độ ortho so với điểm gốc.</summary>
    public static Pt ApplyOrtho(Pt from, Pt to)
    {
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        return dx >= dy ? new Pt(to.X, from.Y) : new Pt(from.X, to.Y);
    }

    /// <summary>
    /// Polar tracking: giữ nguyên khoảng cách nhưng bắt hướng về bội số gần nhất
    /// của <paramref name="incrementDegrees"/> (ví dụ 15° → 0, 15, 30, 45…).
    /// </summary>
    public static Pt ApplyPolar(Pt from, Pt to, double incrementDegrees)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 1e-12) return to;

        var step = incrementDegrees * Math.PI / 180.0;
        if (step <= 1e-9) return to;

        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / step) * step;

        return new Pt(from.X + distance * Math.Cos(snapped),
                      from.Y + distance * Math.Sin(snapped));
    }
}
