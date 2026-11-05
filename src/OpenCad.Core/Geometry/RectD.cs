namespace OpenCad.Core.Geometry;

/// <summary>Hộp bao 2D (world coordinates).</summary>
public readonly struct RectD
{
    public readonly double MinX;
    public readonly double MinY;
    public readonly double MaxX;
    public readonly double MaxY;

    public RectD(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }

    public static RectD Empty => new(double.PositiveInfinity, double.PositiveInfinity,
                                     double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Pt Center => IsEmpty ? Pt.Zero : new Pt((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    public RectD Union(Pt p) => new(
        Math.Min(MinX, p.X), Math.Min(MinY, p.Y),
        Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public RectD Union(RectD o)
    {
        if (o.IsEmpty) return this;
        if (IsEmpty) return o;
        return new RectD(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY),
                         Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));
    }

    public RectD Inflate(double d) => IsEmpty ? this
        : new RectD(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Contains(Pt p) => !IsEmpty && p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Intersects(RectD o) => !IsEmpty && !o.IsEmpty
        && MinX <= o.MaxX && MaxX >= o.MinX
        && MinY <= o.MaxY && MaxY >= o.MinY;

    public static RectD FromPoints(IEnumerable<Pt> pts)
    {
        var r = Empty;
        foreach (var p in pts) r = r.Union(p);
        return r;
    }

    public override string ToString() =>
        IsEmpty ? "<empty>" : $"[{MinX:0.##},{MinY:0.##} .. {MaxX:0.##},{MaxY:0.##}]";
}
