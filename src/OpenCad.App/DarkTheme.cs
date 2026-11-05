using System.Drawing.Drawing2D;

namespace OpenCad.App;

/// <summary>
/// @brief Authentic color palette constants replicating the modern AutoCAD dark interface theme.
/// </summary>
public static class AutoCadTheme
{
    /// <summary>AutoCAD signature brand red (Application button).</summary>
    public static readonly Color AppRed = Color.FromArgb(200, 16, 46);

    /// <summary>AutoCAD application button hover state red.</summary>
    public static readonly Color AppRedHover = Color.FromArgb(229, 27, 36);

    /// <summary>AutoCAD classic canvas model space background (#212830).</summary>
    public static readonly Color CanvasBackground = Color.FromArgb(33, 40, 48);

    /// <summary>AutoCAD paper space background for page border / surround.</summary>
    public static readonly Color PaperSurround = Color.FromArgb(45, 49, 56);

    /// <summary>AutoCAD top application bar / quick access toolbar background.</summary>
    public static readonly Color AppHeaderBackground = Color.FromArgb(31, 34, 39);

    /// <summary>AutoCAD ribbon tab strip background (#212328).</summary>
    public static readonly Color RibbonTabStrip = Color.FromArgb(33, 35, 40);

    /// <summary>AutoCAD active ribbon tab background (#2D3139).</summary>
    public static readonly Color RibbonActiveTab = Color.FromArgb(45, 49, 57);

    /// <summary>AutoCAD ribbon content background (#2D3139).</summary>
    public static readonly Color RibbonContent = Color.FromArgb(45, 49, 57);

    /// <summary>AutoCAD ribbon panel background card.</summary>
    public static readonly Color RibbonPanel = Color.FromArgb(40, 44, 52);

    /// <summary>AutoCAD ribbon panel separator and borders.</summary>
    public static readonly Color RibbonPanelBorder = Color.FromArgb(32, 35, 42);

    /// <summary>AutoCAD ribbon button hover background.</summary>
    public static readonly Color ButtonHover = Color.FromArgb(66, 71, 84);

    /// <summary>AutoCAD electric accent blue for active/pressed toggles and selected tabs (#007ACC).</summary>
    public static readonly Color AccentBlue = Color.FromArgb(0, 122, 204);

    /// <summary>AutoCAD electric blue for pressed buttons and active badges.</summary>
    public static readonly Color ButtonPressed = Color.FromArgb(0, 122, 204);

    /// <summary>AutoCAD side palette background (Properties, Layers).</summary>
    public static readonly Color PaletteBackground = Color.FromArgb(38, 41, 47);

    /// <summary>AutoCAD side palette header background.</summary>
    public static readonly Color PaletteHeader = Color.FromArgb(31, 34, 39);

    /// <summary>AutoCAD command line container background.</summary>
    public static readonly Color CommandLineBackground = Color.FromArgb(32, 35, 42);

    /// <summary>AutoCAD command line border.</summary>
    public static readonly Color CommandLineBorder = Color.FromArgb(56, 61, 72);

    /// <summary>AutoCAD status bar background.</summary>
    public static readonly Color StatusBarBackground = Color.FromArgb(24, 26, 30);

    /// <summary>AutoCAD status bar active toggle blue.</summary>
    public static readonly Color StatusActiveBlue = Color.FromArgb(0, 122, 204);

    /// <summary>Primary bright text color.</summary>
    public static readonly Color TextPrimary = Color.FromArgb(240, 242, 245);

    /// <summary>Secondary muted text color.</summary>
    public static readonly Color TextSecondary = Color.FromArgb(154, 162, 175);

    /// <summary>Subtle border and divider line color.</summary>
    public static readonly Color BorderSubtle = Color.FromArgb(48, 52, 60);

    /// <summary>Cyan highlight for CAD coordinates and prompt badges.</summary>
    public static readonly Color CyanAccent = Color.FromArgb(0, 200, 255);
}

/// <summary>
/// @brief Professional dark color table replicating AutoCAD toolstrip and menu styling.
/// </summary>
internal sealed class DarkColorTable : ProfessionalColorTable
{
    private static readonly Color Background = Color.FromArgb(40, 44, 52);
    private static readonly Color Hover = Color.FromArgb(62, 68, 78);
    private static readonly Color Pressed = Color.FromArgb(0, 122, 204);
    private static readonly Color Border = Color.FromArgb(28, 31, 36);
    private static readonly Color Separator = Color.FromArgb(56, 61, 72);

    public override Color ToolStripGradientBegin => Background;
    public override Color ToolStripGradientMiddle => Background;
    public override Color ToolStripGradientEnd => Background;
    public override Color ToolStripBorder => Border;
    public override Color ToolStripDropDownBackground => Color.FromArgb(33, 36, 42);

    public override Color ButtonSelectedGradientBegin => Hover;
    public override Color ButtonSelectedGradientMiddle => Hover;
    public override Color ButtonSelectedGradientEnd => Hover;
    public override Color ButtonSelectedBorder => Hover;

    public override Color ButtonPressedGradientBegin => Pressed;
    public override Color ButtonPressedGradientMiddle => Pressed;
    public override Color ButtonPressedGradientEnd => Pressed;
    public override Color ButtonPressedBorder => Pressed;

    public override Color ButtonCheckedGradientBegin => Pressed;
    public override Color ButtonCheckedGradientMiddle => Pressed;
    public override Color ButtonCheckedGradientEnd => Pressed;

    public override Color SeparatorDark => Separator;
    public override Color SeparatorLight => Separator;

    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Background;
    public override Color MenuItemPressedGradientEnd => Background;

    public override Color MenuItemBorder => Separator;
    public override Color MenuBorder => Border;

    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;

    public override Color StatusStripGradientBegin => AutoCadTheme.StatusBarBackground;
    public override Color StatusStripGradientEnd => AutoCadTheme.StatusBarBackground;
}

/// <summary>
/// @brief Custom ToolStrip renderer providing AutoCAD-style flat dark menus with clean borders.
/// </summary>
internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    /// <summary>
    /// @brief Initializes a new instance of the DarkToolStripRenderer class.
    /// </summary>
    public DarkToolStripRenderer() : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    /// <summary>
    /// @brief Customizes toolstrip background rendering with flat colors.
    /// @param e Event arguments containing graphics context and affected bounds.
    /// </summary>
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    /// <summary>
    /// @brief Renders crisp dark borders matching AutoCAD window aesthetics.
    /// @param e Event arguments containing graphics context and affected bounds.
    /// </summary>
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(28, 31, 36));
        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, rect);
    }
}
