using OpenCad.Core.Commands;

namespace OpenCad.App;

/// <summary>
/// @brief Authentic AutoCAD-styled Ribbon Bar hosting grouped CAD command tools across specialized tabs.
/// @details Implements a dual-level ribbon hierarchy consisting of top category tabs (Home, Insert,
/// Annotate, View, Output) and card panels (Draw, Modify, Layers, etc.) featuring prominent primary buttons
/// and compact secondary buttons with high-DPI vector icons, AutoCAD tooltips, and dynamic i18n support.
/// </summary>
internal sealed partial class RibbonBar : Panel
{
    private readonly Panel _tabStrip = new();
    private readonly Panel _content = new();
    private readonly List<(Button Tab, Panel Page, string Key, List<(Panel GroupPanel, Label TitleLabel, string GroupKey)> Groups)> _pages = new();

    /// <summary>
    /// @brief Tooltip provider for displaying AutoCAD command names and shortcut hints.
    /// </summary>
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 400,
        ReshowDelay = 200,
        AutoPopDelay = 5000,
        BackColor = Color.FromArgb(38, 41, 48),
        ForeColor = Color.White,
    };

    /// <summary>
    /// @brief Event raised when a ribbon command button is clicked by the user.
    /// </summary>
    public event Action<string>? CommandClicked;

    /// <summary>
    /// @brief Initializes a new instance of the RibbonBar class.
    /// </summary>
    public RibbonBar()
    {
        Dock = DockStyle.Top;
        Height = 112;
        BackColor = AutoCadTheme.RibbonContent;

        _tabStrip.Dock = DockStyle.Top;
        _tabStrip.Height = 28;
        _tabStrip.BackColor = AutoCadTheme.RibbonTabStrip;
        _tabStrip.Padding = new Padding(6, 0, 6, 0);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = AutoCadTheme.RibbonContent;

        Controls.Add(_content);
        Controls.Add(_tabStrip);

        Build();
        I18n.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// @brief Gets the title of the currently active ribbon tab (used for UI self-tests).
    /// </summary>
    public string ActiveTab { get; private set; } = string.Empty;

    /// <summary>
    /// @brief Gets all command names hosted on the ribbon across all tabs.
    /// </summary>
    public IReadOnlyList<string> AllCommandNames =>
        _pages
            .SelectMany(p => ButtonsIn(p.Page))
            .Select(b => b.Tag as string)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

    /// <summary>
    /// @brief Gets the list of current tab names.
    /// </summary>
    public IReadOnlyList<string> TabNames => _pages.Select(p => p.Tab.Text).ToList();

    /// <summary>
    /// @brief Gets the number of visible command buttons on the currently active tab.
    /// </summary>
    public int VisibleButtonCount
    {
        get
        {
            var page = _pages.FirstOrDefault(p => p.Tab.Text == ActiveTab).Page;
            return page is null ? 0 : ButtonsIn(page).Count();
        }
    }

    /// <summary>
    /// @brief Gets all buttons positioned on the currently active tab in left-to-right order.
    /// </summary>
    public IReadOnlyList<Button> ButtonsOnActiveTab
    {
        get
        {
            var page = _pages.FirstOrDefault(p => p.Tab.Text == ActiveTab).Page;
            return page is null ? Array.Empty<Button>() : ButtonsIn(page).ToList();
        }
    }

    /// <summary>
    /// @brief Recursively retrieves all Button controls inside a parent container.
    /// @param parent The parent control to inspect.
    /// @return Enumeration of Button controls.
    /// </summary>
    private static IEnumerable<Button> ButtonsIn(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button) yield return button;
            foreach (var nested in ButtonsIn(child)) yield return nested;
        }
    }

    /// <summary>
    /// @brief Dispatches a command as if its ribbon button was pressed (used for automated tests).
    /// @param name Canonical AutoCAD command name.
    /// </summary>
    internal void RaiseCommand(string name) => CommandClicked?.Invoke(name);

    /// <summary>
    /// @brief Switches the active ribbon tab by matching localized name or canonical key.
    /// @param name The tab name or key to display.
    /// </summary>
    public void ShowTab(string name)
    {
        foreach (var (tab, page, key, _) in _pages)
        {
            var active = string.Equals(tab.Text, name, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, name, StringComparison.OrdinalIgnoreCase) ||
                         (name == "Home" && key == "Home") ||
                         (name == "Annotate" && key == "Annotate");

            page.Visible = active;
            tab.BackColor = active ? AutoCadTheme.RibbonActiveTab : AutoCadTheme.RibbonTabStrip;
            tab.ForeColor = active ? Color.White : Color.FromArgb(160, 168, 180);

            if (active) ActiveTab = tab.Text;
        }
    }

    /// <summary>
    /// @brief Handles runtime language changes by updating tab texts and group titles.
    /// </summary>
    private void OnLanguageChanged()
    {
        foreach (var (tab, _, key, groups) in _pages)
        {
            var localizedTitle = I18n.T($"Ribbon.Tab.{key}");
            if (tab.Text == ActiveTab) ActiveTab = localizedTitle;
            tab.Text = localizedTitle;

            foreach (var (_, titleLabel, groupKey) in groups)
            {
                titleLabel.Text = I18n.T($"Ribbon.Group.{groupKey}");
            }
        }
    }

    /// <summary>
    /// @brief Constructs all default AutoCAD ribbon tabs, groups, and command buttons.
    /// </summary>
    private void Build()
    {
        _pages.Clear();
        _tabStrip.Controls.Clear();
        _content.Controls.Clear();

        // 1. Home Tab (Primary AutoCAD drafting tools)
        AddTab("Home", "Home", new[]
        {
            Group("Draw", "Draw", new[] { "LINE", "PLINE", "RECTANG", "CIRCLE", "ARC", "ELLIPSE", "POLYGON" }),
            Group("Modify", "Modify", new[] { "MOVE", "COPY", "ROTATE", "SCALE", "MIRROR", "OFFSET", "TRIM", "ERASE" }),
            Group("Layers", "Layers", new[] { "MATCHPROP", "HATCHEDIT" }),
        });

        // 2. Insert Tab (Blocks, references, attributes, hatches)
        AddTab("Insert", "Insert", new[]
        {
            Group("Block", "Block", new[] { "BLOCK", "INSERT", "BLOCKS", "ATTDEF" }),
            Group("Hatch", "Hatch", new[] { "HATCH", "HATCHEDIT" }),
        });

        // 3. Annotate Tab (Text, dimensions, multi-leaders, inquiry)
        AddTab("Annotate", "Annotate", new[]
        {
            Group("Text", "Text", new[] { "TEXT", "MTEXT", "DDEDIT" }),
            Group("Dimensions", "Dimensions", new[] { "DIMLINEAR", "DIMALIGNED", "DIMANGULAR", "DIMRADIUS", "DIMDIAMETER", "DIMSTYLE" }),
            Group("Inquiry", "Inquiry", new[] { "DIST", "AREA", "DIVIDE", "MEASURE" }),
        });

        // 4. View Tab (Navigation, toggles, audit & management)
        AddTab("View", "View", new[]
        {
            Group("Navigate", "Navigate", new[] { "ZOOMALL" }),
            Group("Drafting", "Drafting", new[] { "OSNAP", "ORTHO", "POLAR" }),
            Group("Manage", "Manage", new[] { "AUDIT", "PURGE", "UNSUPPORTED", "HELP" }),
        });

        // 5. Output Tab (Publishing, PDF, layouts, file operations)
        AddTab("Output", "Output", new[]
        {
            Group("Publish", "Publish", new[] { "PDF", "PLOT" }),
            Group("Layout", "Layout", new[] { "VPORT", "VPSCALE" }),
            Group("File", "File", new[] { "SAVE", "SAVEAS" }),
        });

        // Default open tab: Home / Trang chủ
        ShowTab(I18n.T("Ribbon.Tab.Home"));
    }

    /// <summary>
    /// @brief Appends a tab to the ribbon tab bar and registers its panel groups.
    /// @param key Canonical key representing the tab (e.g. "Home").
    /// @param defaultTitle Default fallback title string.
    /// @param groups Collection of panel group descriptors.
    /// </summary>
    private void AddTab(string key, string defaultTitle, IReadOnlyList<(Panel GroupPanel, Label TitleLabel, string GroupKey)> groups)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = AutoCadTheme.RibbonContent,
        };

        var x = 8;
        foreach (var (groupPanel, _, _) in groups)
        {
            groupPanel.Left = x;
            groupPanel.Top = 3;
            page.Controls.Add(groupPanel);
            x += groupPanel.Width + 6;
        }

        var localizedTitle = I18n.T($"Ribbon.Tab.{key}");
        if (string.IsNullOrEmpty(localizedTitle) || localizedTitle == $"Ribbon.Tab.{key}")
            localizedTitle = defaultTitle;

        var tab = new Button
        {
            Text = localizedTitle,
            Dock = DockStyle.Left,
            Width = 96,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = AutoCadTheme.RibbonTabStrip,
            ForeColor = Color.FromArgb(160, 168, 180),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            TabStop = false,
        };

        tab.FlatAppearance.BorderSize = 0;
        tab.FlatAppearance.MouseOverBackColor = AutoCadTheme.RibbonActiveTab;
        tab.Click += (_, _) => ShowTab(key);

        _tabStrip.Controls.Add(tab);
        _tabStrip.Controls.SetChildIndex(tab, 0);
        _content.Controls.Add(page);

        _pages.Add((tab, page, key, groups.ToList()));
    }
}
