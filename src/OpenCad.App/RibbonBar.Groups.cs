using OpenCad.Core.Commands;

namespace OpenCad.App;

internal sealed partial class RibbonBar
{
    /// <summary>
    /// @brief Creates an AutoCAD-styled command panel group with a footer title.
    /// @param key Canonical group key (e.g. "Draw").
    /// @param defaultTitle Default fallback title.
    /// @param commands List of command names hosted in the group.
    /// @return Tuple containing group panel, title label, and canonical key.
    /// </summary>
    private (Panel GroupPanel, Label TitleLabel, string GroupKey) Group(string key, string defaultTitle, IReadOnlyList<string> commands)
    {
        // Calculate width: large primary buttons take 46px, standard take 38px
        var width = Math.Max(76, commands.Count * 40 + 14);

        var group = new Panel
        {
            Width = width,
            Height = 78,
            BackColor = AutoCadTheme.RibbonPanel,
            BorderStyle = BorderStyle.None,
        };

        // Panel footer label matching AutoCAD ribbon panel styling
        var localizedTitle = I18n.T($"Ribbon.Group.{key}");
        if (string.IsNullOrEmpty(localizedTitle) || localizedTitle == $"Ribbon.Group.{key}")
            localizedTitle = defaultTitle;

        var label = new Label
        {
            Text = localizedTitle,
            Dock = DockStyle.Bottom,
            Height = 16,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(144, 152, 166),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(34, 38, 45),
        };

        var strip = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AutoCadTheme.RibbonPanel,
        };

        var left = 6;
        foreach (var name in commands)
        {
            var command = CommandRegistry.Find(name);
            if (command is null) continue;

            var glyph = GlyphFor(command);
            var isPrimary = IsPrimaryTool(command.Name);
            var btnWidth = isPrimary ? 38 : 36;
            var btnHeight = 54;

            var button = new Button
            {
                Text = ShortLabel(command),
                Image = glyph is null ? null : (isPrimary ? Icons.Get(glyph.Value, 24) : Icons.Get(glyph.Value, 20)),
                Left = left,
                Top = 4,
                Width = btnWidth,
                Height = btnHeight,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(48, 52, 60),
                ForeColor = AutoCadTheme.TextPrimary,
                Font = new Font("Segoe UI", 6.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.BottomCenter,
                ImageAlign = ContentAlignment.TopCenter,
                TabStop = false,
            };

            button.FlatAppearance.BorderColor = Color.FromArgb(64, 69, 78);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = AutoCadTheme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = AutoCadTheme.ButtonPressed;

            var target = command.Name;
            button.Tag = target;

            // Rich AutoCAD tooltip
            var shortcutHint = command.Aliases.Any() ? $" ({string.Join(", ", command.Aliases)})" : string.Empty;
            _toolTip.SetToolTip(button, $"{command.Name}{shortcutHint}\n{command.Description}");

            button.Click += (_, _) => CommandClicked?.Invoke(target);

            strip.Controls.Add(button);
            left += btnWidth + 2;
        }

        group.Controls.Add(strip);
        group.Controls.Add(label);

        return (group, label, key);
    }

    /// <summary>
    /// @brief Identifies whether a tool is considered a prominent primary command in AutoCAD.
    /// @param name Canonical command name.
    /// @return True for primary tools; false for secondary tools.
    /// </summary>
    private static bool IsPrimaryTool(string name) => name switch
    {
        "LINE" or "PLINE" or "CIRCLE" or "ARC" or "MOVE" or "COPY" or "ROTATE"
        or "TRIM" or "ERASE" or "TEXT" or "DIMLINEAR" or "BLOCK" or "HATCH"
        or "ZOOMALL" or "PDF" => true,
        _ => false,
    };

    /// <summary>
    /// @brief Resolves the appropriate vector glyph for a given command.
    /// @param command CAD command metadata descriptor.
    /// @return Glyph enum value if available; null otherwise.
    /// </summary>
    private static Glyph? GlyphFor(CommandInfo command) => command.Name switch
    {
        "LINE" => Glyph.Line,
        "PLINE" => Glyph.Polyline,
        "RECTANG" => Glyph.Rectangle,
        "CIRCLE" => Glyph.Circle,
        "ARC" => Glyph.Arc,
        "ELLIPSE" => Glyph.Ellipse,
        "POLYGON" => Glyph.Polygon,
        "TEXT" or "MTEXT" or "DDEDIT" => Glyph.Text,
        "HATCH" or "HATCHEDIT" => Glyph.Hatch,
        "DIMLINEAR" or "DIMALIGNED" or "DIMANGULAR" or "DIMRADIUS" or "DIMDIAMETER" or "DIMSTYLE"
            => Glyph.Dimension,
        "INSERT" => Glyph.Insert,
        "BLOCK" or "BLOCKS" or "ATTDEF" => Glyph.Block,
        "MOVE" => Glyph.Move,
        "COPY" => Glyph.Copy,
        "ROTATE" => Glyph.Rotate,
        "SCALE" => Glyph.Scale,
        "MIRROR" => Glyph.Mirror,
        "OFFSET" => Glyph.Offset,
        "TRIM" => Glyph.Trim,
        "EXTEND" => Glyph.Extend,
        "FILLET" => Glyph.Fillet,
        "CHAMFER" => Glyph.Chamfer,
        "ERASE" => Glyph.Erase,
        "ZOOMALL" => Glyph.ZoomExtents,
        "ORTHO" => Glyph.Ortho,
        "OSNAP" => Glyph.Osnap,
        "POLAR" or "GRID" => Glyph.Grid,
        "MATCHPROP" => Glyph.Layers,
        "UNDO" => Glyph.Undo,
        "REDO" => Glyph.Redo,
        "DIST" or "AREA" or "DIVIDE" or "MEASURE" => Glyph.Measure,
        "PDF" or "PLOT" => Glyph.Paper,
        _ => null,
    };

    /// <summary>
    /// @brief Produces a concise button label (maximum 5 characters) for ribbon button display.
    /// @param command CAD command metadata descriptor.
    /// @return Short text label.
    /// </summary>
    private static string ShortLabel(CommandInfo command)
    {
        if (command.Name.Length <= 4) return command.Name;
        var alias = command.Aliases.FirstOrDefault(a => a.Length is >= 1 and <= 4);
        return alias ?? command.Name[..4];
    }
}
