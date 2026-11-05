namespace OpenCad.Core.Colors;

/// <summary>
/// Bảng màu AutoCAD Color Index (ACI) 0..255.
/// 0 = ByBlock, 1..9 = màu cố định, 10..249 = 24 hue x 10 sắc độ, 250..255 = thang xám.
/// </summary>
public static class AciPalette
{
    private static readonly Rgb[] Fixed =
    {
        new(255, 255, 255), // 0  ByBlock  -> hiển thị như trắng
        new(255, 0, 0),     // 1  red
        new(255, 255, 0),   // 2  yellow
        new(0, 255, 0),     // 3  green
        new(0, 255, 255),   // 4  cyan
        new(0, 0, 255),     // 5  blue
        new(255, 0, 255),   // 6  magenta
        new(255, 255, 255), // 7  white / black
        new(128, 128, 128), // 8  dark grey
        new(192, 192, 192), // 9  light grey
    };

    private static readonly Rgb[] Grays =
    {
        new(51, 51, 51), new(80, 80, 80), new(105, 105, 105),
        new(130, 130, 130), new(190, 190, 190), new(255, 255, 255),
    };

    // (saturation, value) cho 10 sắc độ trong mỗi nhóm hue
    private static readonly (double S, double V)[] Shades =
    {
        (1.00, 1.00), (0.50, 1.00),
        (1.00, 0.65), (0.50, 0.65),
        (1.00, 0.50), (0.50, 0.50),
        (1.00, 0.30), (0.50, 0.30),
        (1.00, 0.15), (0.50, 0.15),
    };

    public static Rgb FromAci(int index)
    {
        if (index <= 0) return Fixed[0];
        if (index <= 9) return Fixed[index];
        if (index >= 250) return Grays[Math.Clamp(index - 250, 0, 5)];

        var i = index - 10;
        var hue = (i / 10) * 15.0;          // 0, 15, 30 ... 345
        var (s, v) = Shades[i % 10];
        return FromHsv(hue, s, v);
    }

    /// <summary>HSV (hue độ 0..360, s/v 0..1) sang RGB.</summary>
    public static Rgb FromHsv(double hueDeg, double s, double v)
    {
        hueDeg = ((hueDeg % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((hueDeg / 60.0) % 2 - 1));
        var m = v - c;

        double r, g, b;
        if (hueDeg < 60) { r = c; g = x; b = 0; }
        else if (hueDeg < 120) { r = x; g = c; b = 0; }
        else if (hueDeg < 180) { r = 0; g = c; b = x; }
        else if (hueDeg < 240) { r = 0; g = x; b = c; }
        else if (hueDeg < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new Rgb(
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255));
    }
}
