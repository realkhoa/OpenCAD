namespace OpenCad.Core.Geometry;

/// <summary>Điểm 2D bất biến (world coordinates).</summary>
public readonly struct Pt : IEquatable<Pt>
{
    public readonly double X;
    public readonly double Y;

    public Pt(double x, double y) { X = x; Y = y; }

    public static Pt Zero => new(0, 0);

    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new(a.X * k, a.Y * k);
    public static Pt operator /(Pt a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Pt Normalized()
    {
        var l = Length;
        return l < 1e-12 ? Zero : new Pt(X / l, Y / l);
    }

    /// <summary>Vuông góc 90° ngược chiều kim đồng hồ.</summary>
    public Pt Perp() => new(-Y, X);

    public static double Dot(Pt a, Pt b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Pt a, Pt b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Pt a, Pt b) => (a - b).Length;

    public static Pt Lerp(Pt a, Pt b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public bool Equals(Pt other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Pt p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
