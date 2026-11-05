namespace OpenCad.Core.Geometry;

/// <summary>
/// Biến đổi affine 2D: x' = m11*x + m12*y + dx, y' = m21*x + m22*y + dy.
/// Dùng cho block reference (INSERT) với scale + rotation + translation.
/// </summary>
public readonly struct Affine2
{
    public readonly double M11, M12, M21, M22, Dx, Dy;

    public Affine2(double m11, double m12, double m21, double m22, double dx, double dy)
    {
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; Dx = dx; Dy = dy;
    }

    public static Affine2 Identity => new(1, 0, 0, 1, 0, 0);

    public Pt Apply(Pt p) => new(M11 * p.X + M12 * p.Y + Dx,
                                 M21 * p.X + M22 * p.Y + Dy);

    /// <summary>Chỉ áp dụng phần tuyến tính (bỏ tịnh tiến) — dùng cho vector hướng.</summary>
    public Pt ApplyVector(Pt v) => new(M11 * v.X + M12 * v.Y, M21 * v.X + M22 * v.Y);

    public bool IsIdentity => Math.Abs(M11 - 1) < 1e-12 && Math.Abs(M12) < 1e-12
                           && Math.Abs(M21) < 1e-12 && Math.Abs(M22 - 1) < 1e-12
                           && Math.Abs(Dx) < 1e-12 && Math.Abs(Dy) < 1e-12;

    public double ScaleFactor => Math.Sqrt(Math.Abs(M11 * M22 - M12 * M21));

    public static Affine2 Translate(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    /// <summary>Góc xoay của biến đổi (radian).</summary>
    public double RotationAngle => Math.Atan2(M21, M11);

    /// <summary>Nghịch đảo. Trả về Identity nếu suy biến.</summary>
    public Affine2 Inverse()
    {
        var det = M11 * M22 - M12 * M21;
        if (Math.Abs(det) < 1e-18) return Identity;

        var i11 = M22 / det;
        var i12 = -M12 / det;
        var i21 = -M21 / det;
        var i22 = M11 / det;

        return new Affine2(i11, i12, i21, i22,
                           -(i11 * Dx + i12 * Dy),
                           -(i21 * Dx + i22 * Dy));
    }

    public static Affine2 Rotate(double angleRad, Pt center)
    {
        var c = Math.Cos(angleRad);
        var s = Math.Sin(angleRad);
        return new Affine2(c, -s, s, c,
                           center.X - c * center.X + s * center.Y,
                           center.Y - s * center.X - c * center.Y);
    }

    public static Affine2 Scale(double factor, Pt center)
        => new(factor, 0, 0, factor,
               center.X * (1 - factor), center.Y * (1 - factor));

    /// <summary>Đối xứng qua đường thẳng đi qua a và b.</summary>
    public static Affine2 Mirror(Pt a, Pt b)
    {
        var d = (b - a).Normalized();
        if (d.LengthSquared < 1e-18) return Identity;

        var m11 = d.X * d.X - d.Y * d.Y;
        var m12 = 2 * d.X * d.Y;
        var m21 = 2 * d.X * d.Y;
        var m22 = d.Y * d.Y - d.X * d.X;

        return new Affine2(m11, m12, m21, m22,
                           a.X - (m11 * a.X + m12 * a.Y),
                           a.Y - (m21 * a.X + m22 * a.Y));
    }

    /// <summary>Tạo transform cho INSERT: scale → rotate → translate.</summary>
    public static Affine2 ForInsert(double sx, double sy, double rotationRad, Pt insertPoint)
    {
        var c = Math.Cos(rotationRad);
        var s = Math.Sin(rotationRad);
        return new Affine2(c * sx, -s * sy,
                           s * sx, c * sy,
                           insertPoint.X, insertPoint.Y);
    }

    /// <summary>this ∘ other — áp dụng other trước, rồi this.</summary>
    public Affine2 After(Affine2 other) => new(
        M11 * other.M11 + M12 * other.M21,
        M11 * other.M12 + M12 * other.M22,
        M21 * other.M11 + M22 * other.M21,
        M21 * other.M12 + M22 * other.M22,
        M11 * other.Dx + M12 * other.Dy + Dx,
        M21 * other.Dx + M22 * other.Dy + Dy);
}
