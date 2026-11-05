namespace OpenCad.Core.Colors;

/// <summary>Màu RGBA độc lập nền tảng (không phụ thuộc System.Drawing).</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte A, R, G, B;

    public Rgb(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

    public static Rgb White => new(255, 255, 255);
    public static Rgb Black => new(0, 0, 0);

    public Rgb WithAlpha(byte a) => new(R, G, B, a);

    public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B && A == o.A;
    public override bool Equals(object? obj) => obj is Rgb c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
