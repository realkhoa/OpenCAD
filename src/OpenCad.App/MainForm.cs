using OpenCad.App.Panels;
using OpenCad.Core;
using OpenCad.Core.Editing;
using OpenCad.Core.Geometry;

namespace OpenCad.App;

public sealed class MainForm : Form
{
    private readonly CadCanvas _canvas = new();
    private readonly LayerPanel _layers = new();
    private readonly PropertiesPanel _props = new();
    private readonly CommandLine _command = new();

    private readonly StatusStrip _status = new();
    private readonly RibbonBar _ribbon = new();
    private readonly Panel _appHeader = new();
    private readonly Button _appMenuButton = new();
    private readonly ToolStrip _qat = new();
    private readonly ToolStripButton _langToggle = new();
    private readonly ToolStripLabel _workspaceLabel = new();
    private OpenCad.Core.Export.PlotStyleTable? _plotStyle;
    private readonly ToolStripMenuItem _recentMenu = new();
    private readonly ListBox _historyList = new();
    private readonly FlowLayoutPanel _layoutBar = new();
    private readonly ToolStripComboBox _layerCombo = new();
    private readonly ContextMenuStrip _canvasMenu = new();
    private string? _activeLayout;
    private string _lastCommand = "LINE";
    private readonly ToolStripStatusLabel _statusCoords = new() { Text = "X 0  Y 0" };
    private readonly ToolStripStatusLabel _statusInfo = new() { Text = string.Empty };
    private readonly ToolStripStatusLabel _toggleOsnap = new() { Text = "OSNAP" };
    private readonly ToolStripStatusLabel _toggleOrtho = new() { Text = "ORTHO" };
    private readonly ToolStripStatusLabel _togglePolar = new() { Text = "POLAR" };
    private readonly ToolStripStatusLabel _toggleGrid = new() { Text = "GRID" };
    private readonly ToolStripStatusLabel _toggleLwt = new() { Text = "LWT" };

    private CadEditor _editor = null!;
    private readonly string? _initialPath;
    private ToolStripMenuItem _undoItem = null!;
    private ToolStripMenuItem _redoItem = null!;
    private string? _statusHint;

    public MainForm(string? initialPath = null)
    {
        _initialPath = initialPath;

        Text = "OpenCAD";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        BuildCanvasMenu();

        Load += (_, _) =>
        {
            OnFirstLoad();
            StartAutosave();
        };
        FormClosing += OnFormClosing;
    }

    // ----------------------------------------------------------------- UI

    private void BuildLayout()
    {
        var menu = BuildMenu();

        _layers.Dock = DockStyle.Left;
        _props.Dock = DockStyle.Right;
        _command.Dock = DockStyle.Bottom;

        _layoutBar.Dock = DockStyle.Bottom;
        _layoutBar.Height = 30;
        _layoutBar.FlowDirection = FlowDirection.LeftToRight;
        _layoutBar.WrapContents = false;
        _layoutBar.AutoScroll = true;
        _layoutBar.BackColor = Color.FromArgb(30, 33, 38);
        _layoutBar.Padding = new Padding(6, 3, 6, 3);

        _historyList.Dock = DockStyle.Bottom;
        _historyList.Height = 110;
        _historyList.BorderStyle = BorderStyle.None;
        _historyList.BackColor = Color.FromArgb(24, 26, 30);
        _historyList.ForeColor = Color.FromArgb(180, 190, 200);
        _historyList.Font = new Font("Consolas", 9f);
        _historyList.IntegralHeight = false;
        _historyList.Visible = false;

        // Bấm được như AutoCAD: con trỏ bàn tay + gợi ý phím tắt
        _toggleOsnap.IsLink = true;
        _toggleOrtho.IsLink = true;
        _togglePolar.IsLink = true;
        _toggleGrid.IsLink = true;

        _toggleOsnap.ToolTipText = "Bắt điểm (F3)";
        _toggleOrtho.ToolTipText = "Trực giao (F8)";
        _togglePolar.ToolTipText = "Rãnh căn 15° (F10)";
        _toggleGrid.ToolTipText = "Lưới (F7)";

        _toggleLwt.IsLink = true;
        _toggleLwt.ToolTipText = "Hiện độ đậm nét (LWT)";

        _status.Items.Add(_statusInfo);
        _status.Items.Add(_toggleOsnap);
        _status.Items.Add(_toggleOrtho);
        _status.Items.Add(_togglePolar);
        _status.Items.Add(_toggleGrid);
        _status.Items.Add(_toggleLwt);
        _status.Items.Add(_statusCoords);
        _status.Dock = DockStyle.Bottom;

        _canvas.Dock = DockStyle.Fill;

        // AutoCAD Top Header & Quick Access Toolbar
        _appHeader.Dock = DockStyle.Top;
        _appHeader.Height = 32;
        _appHeader.BackColor = AutoCadTheme.AppHeaderBackground;
        _appHeader.Padding = new Padding(3, 2, 3, 2);

        _appMenuButton.Dock = DockStyle.Left;
        _appMenuButton.Width = 32;
        _appMenuButton.Height = 28;
        _appMenuButton.FlatStyle = FlatStyle.Flat;
        _appMenuButton.FlatAppearance.BorderSize = 0;
        _appMenuButton.BackColor = AutoCadTheme.AppRed;
        _appMenuButton.Image = Icons.Get(Glyph.AppMenu, 22);
        _appMenuButton.TabStop = false;
        _appMenuButton.Click += (_, _) => ShowAppMenu();

        _qat.Dock = DockStyle.Fill;
        _qat.BackColor = Color.Transparent;
        _qat.GripStyle = ToolStripGripStyle.Hidden;
        _qat.Renderer = new DarkToolStripRenderer();

        AddQatButton("&New", Icons.Get(Glyph.Line, 18), "New Drawing (Ctrl+N)", () => NewDocument());
        AddQatButton("&Open", Icons.Get(Glyph.Insert, 18), "Open Drawing (Ctrl+O)", () => OpenDocument());
        AddQatButton("&Save", Icons.Get(Glyph.Paper, 18), "Save Drawing (Ctrl+S)", () => SaveDocument());
        AddQatButton("&Plot", Icons.Get(Glyph.Paper, 18), "Plot / Export PDF (Ctrl+P)", () => ExportPdf(420, 297, null));
        _qat.Items.Add(new ToolStripSeparator());
        AddQatButton("&Undo", Icons.Get(Glyph.Undo, 18), "Undo (Ctrl+Z)", () => { UndoAction(); UpdateStatus(); });
        AddQatButton("&Redo", Icons.Get(Glyph.Redo, 18), "Redo (Ctrl+Y)", () => { RedoAction(); UpdateStatus(); });
        _qat.Items.Add(new ToolStripSeparator());

        _workspaceLabel.Text = I18n.T("App.Workspace");
        _workspaceLabel.ForeColor = AutoCadTheme.TextSecondary;
        _workspaceLabel.Font = new Font("Segoe UI", 8.5f);
        _qat.Items.Add(_workspaceLabel);
        _qat.Items.Add(new ToolStripSeparator());

        _langToggle.Text = I18n.CurrentLanguage == AppLanguage.English ? "🌐 EN" : "🌐 VI";
        _langToggle.ToolTipText = "Switch Language / Đổi ngôn ngữ (LANG)";
        _langToggle.ForeColor = AutoCadTheme.CyanAccent;
        _langToggle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _langToggle.Click += (_, _) => ToggleLanguage();
        _qat.Items.Add(_langToggle);

        _appHeader.Controls.Add(_qat);
        _appHeader.Controls.Add(_appMenuButton);

        I18n.LanguageChanged += OnLanguageChanged;

        // Thứ tự Add quyết định thứ tự dock: thêm sau = ra ngoài
        Controls.Add(_canvas);
        Controls.Add(_layers);
        Controls.Add(_props);
        Controls.Add(_layoutBar);
        Controls.Add(_historyList);
        Controls.Add(_command);
        Controls.Add(_status);
        Controls.Add(_ribbon);
        Controls.Add(menu);
        Controls.Add(_appHeader);

        MainMenuStrip = menu;
    }

    /// <summary>Phím tắt clipboard: Ctrl+C copy, Ctrl+V paste.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C))
        {
            _canvas.CopySelectionToClipboard();
            UpdateStatus();
            return true;
        }

        if (keyData == (Keys.Control | Keys.V))
        {
            _canvas.PasteFromClipboard();
            SyncSelection();
            return true;
        }

        // Hoàn tác / làm lại: xử lý tường minh để không phụ thuộc trạng thái menu
        if (keyData == (Keys.Control | Keys.Z))
        {
            UndoAction();
            UpdateStatus();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y))
        {
            RedoAction();
            UpdateStatus();
            return true;
        }

        if (keyData == (Keys.Control | Keys.A))
        {
            _canvas.SelectAll();
            SyncSelection();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            _canvas.Cancel();
            _canvas.ClearSelection();
            SyncSelection();
            UpdateStatus();
            return true;
        }

        if (keyData == Keys.Delete)
        {
            _canvas.DeleteSelection();
            SyncSelection();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// @brief Adds an action button to the Quick Access Toolbar (QAT).
    /// </summary>
    private void AddQatButton(string text, Image? image, string toolTip, Action onClick)
    {
        var btn = new ToolStripButton(text, image, (_, _) => onClick())
        {
            DisplayStyle = image is not null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.Text,
            ToolTipText = toolTip,
        };
        _qat.Items.Add(btn);
    }

    /// <summary>
    /// @brief Displays the AutoCAD Application Menu when the top-left Red "A" button is clicked.
    /// </summary>
    private void ShowAppMenu()
    {
        var appMenu = new ContextMenuStrip
        {
            Renderer = new DarkToolStripRenderer(),
            BackColor = AutoCadTheme.RibbonContent,
            ForeColor = AutoCadTheme.TextPrimary,
        };

        appMenu.Items.Add(Item(I18n.T("Menu.New"), Keys.Control | Keys.N, (_, _) => NewDocument()));
        appMenu.Items.Add(Item(I18n.T("Menu.Open"), Keys.Control | Keys.O, (_, _) => OpenDocument()));
        appMenu.Items.Add(new ToolStripSeparator());
        appMenu.Items.Add(Item(I18n.T("Menu.Save"), Keys.Control | Keys.S, (_, _) => SaveDocument()));
        appMenu.Items.Add(Item(I18n.T("Menu.SaveAs"), Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveDocumentAs()));
        appMenu.Items.Add(new ToolStripSeparator());

        var exportItem = new ToolStripMenuItem(I18n.T("Menu.ExportPdf"));
        exportItem.DropDownItems.Add(Item("A3 (Fit)", Keys.Control | Keys.P, (_, _) => ExportPdf(420, 297, null)));
        exportItem.DropDownItems.Add(Item("A3 (1:50)", Keys.None, (_, _) => ExportPdf(420, 297, 50)));
        exportItem.DropDownItems.Add(Item("A3 (1:100)", Keys.None, (_, _) => ExportPdf(420, 297, 100)));
        appMenu.Items.Add(exportItem);

        appMenu.Items.Add(new ToolStripSeparator());
        var langItem = new ToolStripMenuItem(I18n.T("App.Language"));
        langItem.DropDownItems.Add(Item("English", Keys.None, (_, _) => I18n.CurrentLanguage = AppLanguage.English));
        langItem.DropDownItems.Add(Item("Tiếng Việt", Keys.None, (_, _) => I18n.CurrentLanguage = AppLanguage.Vietnamese));
        appMenu.Items.Add(langItem);

        appMenu.Items.Add(new ToolStripSeparator());
        appMenu.Items.Add(Item(I18n.T("Menu.Exit"), Keys.Alt | Keys.F4, (_, _) => Close()));

        appMenu.Show(_appMenuButton, new Point(0, _appMenuButton.Height));
    }

    /// <summary>
    /// @brief Toggles between English and Vietnamese language options.
    /// </summary>
    private void ToggleLanguage()
    {
        I18n.CurrentLanguage = I18n.CurrentLanguage == AppLanguage.English
            ? AppLanguage.Vietnamese
            : AppLanguage.English;
    }

    /// <summary>
    /// @brief Updates main form UI strings and layout when the application language changes.
    /// </summary>
    private void OnLanguageChanged()
    {
        _langToggle.Text = I18n.CurrentLanguage == AppLanguage.English ? "🌐 EN" : "🌐 VI";
        _workspaceLabel.Text = I18n.T("App.Workspace");
        _toggleOsnap.ToolTipText = I18n.T("Status.Tip.Osnap");
        _toggleOrtho.ToolTipText = I18n.T("Status.Tip.Ortho");
        _togglePolar.ToolTipText = I18n.T("Status.Tip.Polar");
        _toggleGrid.ToolTipText = I18n.T("Status.Tip.Grid");
        _toggleLwt.ToolTipText = I18n.T("Status.Tip.Lwt");

        UpdateTitle();
        UpdateStatus();
        RebuildLayoutTabs();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(45, 48, 54),
            ForeColor = Color.Gainsboro,
            Renderer = new DarkToolStripRenderer(),
        };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&New", Keys.Control | Keys.N, (_, _) => NewDocument()));
        file.DropDownItems.Add(Item("&Open…", Keys.Control | Keys.O, (_, _) => OpenDocument()));
        file.DropDownItems.Add(new ToolStripSeparator());

        _recentMenu.Text = "Mở &gần đây";


        file.DropDownItems.Add(_recentMenu);

        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (_, _) => SaveDocument()));
        file.DropDownItems.Add(Item("Save &As…", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveDocumentAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        var exportMenu = new ToolStripMenuItem("Export &PDF");
        exportMenu.DropDownItems.Add(Item("Vừa trang (A3)", Keys.Control | Keys.P, (_, _) => ExportPdf(420, 297, null)));
        exportMenu.DropDownItems.Add(new ToolStripSeparator());
        exportMenu.DropDownItems.Add(Item("A3 — tỷ lệ 1:50", Keys.None, (_, _) => ExportPdf(420, 297, 50)));
        exportMenu.DropDownItems.Add(Item("A3 — tỷ lệ 1:100", Keys.None, (_, _) => ExportPdf(420, 297, 100)));
        exportMenu.DropDownItems.Add(Item("A3 — tỷ lệ 1:200", Keys.None, (_, _) => ExportPdf(420, 297, 200)));
        exportMenu.DropDownItems.Add(new ToolStripSeparator());
        exportMenu.DropDownItems.Add(Item("A4 — tỷ lệ 1:100", Keys.None, (_, _) => ExportPdf(297, 210, 100)));
        exportMenu.DropDownItems.Add(new ToolStripSeparator());
        exportMenu.DropDownItems.Add(Item("In đơn sắc 1:50 (kiểu CTB)", Keys.None, (_, _) => ExportPdf(420, 297, 50, true)));
        exportMenu.DropDownItems.Add(Item("In đơn sắc 1:100 (kiểu CTB)", Keys.None, (_, _) => ExportPdf(420, 297, 100, true)));
        exportMenu.DropDownItems.Add(new ToolStripSeparator());
        exportMenu.DropDownItems.Add(Item("In tờ có khung tên — A3 1:50…", Keys.None, (_, _) => PlotSheet(50)));
        exportMenu.DropDownItems.Add(Item("In tờ có khung tên — A3 1:100…", Keys.None, (_, _) => PlotSheet(100)));
        exportMenu.DropDownItems.Add(new ToolStripSeparator());
        exportMenu.DropDownItems.Add(Item("In bộ hồ sơ (nhiều file)…", Keys.None, (_, _) => BatchPlot()));

        file.DropDownItems.Add(exportMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", Keys.Alt | Keys.F4, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edit");
        _undoItem = Item("&Undo", Keys.Control | Keys.Z, (_, _) => { UndoAction(); UpdateStatus(); });
        _redoItem = Item("&Redo", Keys.Control | Keys.Y, (_, _) => { RedoAction(); UpdateStatus(); });
        edit.DropDownItems.Add(_undoItem);
        edit.DropDownItems.Add(_redoItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("&Erase", Keys.None, (_, _) => Erase()));
        edit.DropDownItems.Add(Item("Select &All", Keys.Control | Keys.A, (_, _) => _canvas.SelectAll()));

        var draw = new ToolStripMenuItem("&Draw");
        draw.DropDownItems.Add(Item("&Line", Keys.None, (_, _) => SetMode(EditMode.Line)));
        draw.DropDownItems.Add(Item("&Polyline", Keys.None, (_, _) => SetMode(EditMode.Polyline)));
        draw.DropDownItems.Add(Item("&Rectangle", Keys.None, (_, _) => SetMode(EditMode.Rectangle)));
        draw.DropDownItems.Add(Item("&Circle", Keys.None, (_, _) => SetMode(EditMode.Circle)));
        draw.DropDownItems.Add(Item("&Arc", Keys.None, (_, _) => SetMode(EditMode.Arc)));
        draw.DropDownItems.Add(Item("&Text", Keys.None, (_, _) => SetMode(EditMode.Text)));
        draw.DropDownItems.Add(Item("&Hatch", Keys.None, (_, _) => SetMode(EditMode.Hatch)));
        draw.DropDownItems.Add(Item("&Dimension", Keys.None, (_, _) => SetMode(EditMode.Dimension)));
        draw.DropDownItems.Add(Item("Dimension &Radius", Keys.None, (_, _) => SetMode(EditMode.DimensionRadius)));
        draw.DropDownItems.Add(Item("Dimension &Diameter", Keys.None, (_, _) => SetMode(EditMode.DimensionDiameter)));
        draw.DropDownItems.Add(Item("Dimension &Angular", Keys.None, (_, _) => SetMode(EditMode.DimensionAngular)));
        draw.DropDownItems.Add(Item("Dimension &Ordinate", Keys.None, (_, _) => SetMode(EditMode.DimensionOrdinate)));
        draw.DropDownItems.Add(new ToolStripSeparator());
        draw.DropDownItems.Add(Item("&Ellipse", Keys.None, (_, _) => SetMode(EditMode.Ellipse)));
        draw.DropDownItems.Add(Item("&Polygon", Keys.None, (_, _) => SetMode(EditMode.Polygon)));
        draw.DropDownItems.Add(Item("Di&vide", Keys.None, (_, _) => SetMode(EditMode.Divide)));
        draw.DropDownItems.Add(Item("&Measure", Keys.None, (_, _) => SetMode(EditMode.Measure)));
        draw.DropDownItems.Add(Item("&Note on sheet (PTEXT)", Keys.None, (_, _) => SetMode(EditMode.PaperText)));
        draw.DropDownItems.Add(Item("&Line on sheet (SL)", Keys.None, (_, _) => SetMode(EditMode.PaperLine)));
        draw.DropDownItems.Add(Item("&Insert block", Keys.None, (_, _) => SetMode(EditMode.Insert)));
        draw.DropDownItems.Add(Item("&Create block from selection", Keys.None, (_, _) => SetMode(EditMode.BlockCreate)));
        draw.DropDownItems.Add(new ToolStripSeparator());
        draw.DropDownItems.Add(Item("&Distance", Keys.None, (_, _) => SetMode(EditMode.Dist)));
        draw.DropDownItems.Add(Item("&Area", Keys.None, (_, _) => SetMode(EditMode.Area)));

        var modify = new ToolStripMenuItem("&Modify");
        modify.DropDownItems.Add(Item("&Move", Keys.None, (_, _) => SetMode(EditMode.Move)));
        modify.DropDownItems.Add(Item("&Copy", Keys.None, (_, _) => SetMode(EditMode.Copy)));
        modify.DropDownItems.Add(Item("&Rotate", Keys.None, (_, _) => SetMode(EditMode.Rotate)));
        modify.DropDownItems.Add(Item("&Scale", Keys.None, (_, _) => SetMode(EditMode.Scale)));
        modify.DropDownItems.Add(Item("Mirro&r", Keys.None, (_, _) => SetMode(EditMode.Mirror)));
        modify.DropDownItems.Add(Item("&Offset", Keys.None, (_, _) => SetMode(EditMode.Offset)));
        modify.DropDownItems.Add(Item("&Array", Keys.None, (_, _) => SetMode(EditMode.Array)));
        modify.DropDownItems.Add(Item("Array &polar", Keys.None, (_, _) => SetMode(EditMode.ArrayPolar)));
        modify.DropDownItems.Add(Item("S&tretch", Keys.None, (_, _) => SetMode(EditMode.Stretch)));
        modify.DropDownItems.Add(Item("Edit block &attribute", Keys.None, (_, _) => SetMode(EditMode.AttributeEdit)));
        modify.DropDownItems.Add(new ToolStripSeparator());
        modify.DropDownItems.Add(Item("&Trim", Keys.None, (_, _) => SetMode(EditMode.Trim)));
        modify.DropDownItems.Add(Item("E&xtend", Keys.None, (_, _) => SetMode(EditMode.Extend)));
        modify.DropDownItems.Add(new ToolStripSeparator());
        modify.DropDownItems.Add(Item("&Fillet", Keys.None, (_, _) => SetMode(EditMode.Fillet)));
        modify.DropDownItems.Add(Item("&Chamfer", Keys.None, (_, _) => SetMode(EditMode.Chamfer)));

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(Item("Zoom &Extents", Keys.Control | Keys.E, (_, _) => _canvas.ZoomExtents()));
        view.DropDownItems.Add(Item("Zoom &In", Keys.Control | Keys.Oemplus, (_, _) => _canvas.ZoomBy(1.25)));
        view.DropDownItems.Add(Item("Zoom &Out", Keys.Control | Keys.OemMinus, (_, _) => _canvas.ZoomBy(1 / 1.25)));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("Toggle &Ortho (F8)", Keys.F8, (_, _) => { _canvas.ToggleOrtho(); UpdateStatus(); }));
        view.DropDownItems.Add(Item("Toggle &Polar (F10)", Keys.F10, (_, _) => { _canvas.TogglePolar(); UpdateStatus(); }));
        view.DropDownItems.Add(Item("Toggle &Osnap (F3)", Keys.F3, (_, _) => { _canvas.ToggleSnap(); UpdateStatus(); }));
        view.DropDownItems.Add(Item("Toggle &Grid (F7)", Keys.F7, (_, _) => { _canvas.ToggleGrid(); UpdateStatus(); }));
        view.DropDownItems.Add(Item("Line &weight (LWT)", Keys.None, (_, _) => { _canvas.ToggleLineWeights(); UpdateStatus(); }));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("Command &history (F2)", Keys.F2, (_, _) => ToggleHistory()));

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(draw);
        menu.Items.Add(modify);
        menu.Items.Add(view);
        menu.Items.Add(BuildLayoutMenu());

        return menu;
    }

    private ToolStripMenuItem BuildLayoutMenu()
    {
        var layoutMenu = new ToolStripMenuItem("&Layout");
        layoutMenu.DropDownOpening += (_, _) => RebuildLayoutItems(layoutMenu);
        RebuildLayoutItems(layoutMenu);
        return layoutMenu;
    }

    // ------------------------------------------------------- LAYOUT TABS

    private void RebuildLayoutTabs()
    {
        _layoutBar.Controls.Clear();

        AddLayoutTab("Model", null);

        if (_editor is not null)
        {
            foreach (var name in OpenCad.Core.Layout.LayoutFactory.LayoutNames(_editor.Workspace.Document))
                AddLayoutTab(name, name);
        }

        var newButton = new Button
        {
            Text = "+",
            Width = 24,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 45, 52),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(2, 0, 2, 0),
        };
        newButton.FlatAppearance.BorderSize = 0;
        newButton.Click += (_, _) => CreateLayout(50);
        _layoutBar.Controls.Add(newButton);

        UpdateLayoutTabHighlight();
    }

    private void AddLayoutTab(string caption, string? layoutName)
    {
        var button = new Button
        {
            Text = caption,
            AutoSize = false,
            Width = Math.Max(64, caption.Length * 8 + 22),
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 48, 54),
            ForeColor = Color.Gainsboro,
            Margin = new Padding(2, 0, 2, 0),
            Tag = layoutName,
        };

        button.FlatAppearance.BorderColor = Color.FromArgb(70, 75, 85);
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => SelectLayoutTab(layoutName);

        _layoutBar.Controls.Add(button);
    }

    private void SelectLayoutTab(string? layoutName)
    {
        if (_editor is null) return;

        _activeLayout = layoutName;

        if (layoutName is null)
        {
            _canvas.ShowPaper(null);
        }
        else
        {
            var layout = OpenCad.Core.Layout.LayoutFactory.Find(_editor.Workspace.Document, layoutName);
            var sheet = layout is null
                ? null
                : OpenCad.Core.Layout.LayoutFactory.Read(_editor.Workspace.Document, layout);

            _canvas.ShowPaper(sheet, layout?.AssociatedBlock, layoutName ?? string.Empty);
            Log($"Xem layout: {layoutName}");
        }

        UpdateLayoutTabHighlight();
        UpdateStatus();
    }

    private void UpdateLayoutTabHighlight()
    {
        foreach (var control in _layoutBar.Controls.OfType<Button>())
        {
            var isActive = Equals(control.Tag, _activeLayout);
            if (control.Text == "+") continue;
            control.BackColor = isActive ? AutoCadTheme.CanvasBackground : Color.FromArgb(42, 45, 52);
            control.FlatAppearance.BorderColor = isActive ? AutoCadTheme.AccentBlue : Color.FromArgb(60, 65, 75);
            control.Font = new Font("Segoe UI", 8f, isActive ? FontStyle.Bold : FontStyle.Regular);
            control.ForeColor = isActive ? Color.White : Color.Gainsboro;
        }
    }

    // ------------------------------------------------------- LAYER COMBO

    private void RefreshLayerCombo()
    {
        if (_editor is null) return;


        var current = _layerCombo.SelectedItem as string
                      ?? _canvas.CurrentLayer?.Name;

        _layerCombo.Items.Clear();

        foreach (var name in _editor.Workspace.LayerNames())
            _layerCombo.Items.Add(name);

        if (current is not null && _layerCombo.Items.Contains(current))
            _layerCombo.SelectedItem = current;
        else if (_layerCombo.Items.Count > 0)
            _layerCombo.SelectedIndex = 0;

    }


    // --------------------------------------------------- CONTEXT MENU

    private void BuildCanvasMenu()
    {
        _canvasMenu.Opening += OnCanvasMenuOpening;
        _canvas.ContextMenuStrip = _canvasMenu;
    }

    private void OnCanvasMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Đang chạy lệnh thì chuột phải = huỷ lệnh, không mở menu
        if (_canvas.Mode != EditMode.Select || _canvas.AwaitingInput)
        {
            e.Cancel = true;
            _canvas.Cancel();
            _canvas.Focus();
            return;
        }

        _canvasMenu.Items.Clear();

        var hasSelection = _canvas.Selection.Count > 0;

        _canvasMenu.Items.Add(MenuItem("Lặp lại lệnh trước", (_, _) => RunCommand(_lastCommand), !string.IsNullOrEmpty(_lastCommand)));
        _canvasMenu.Items.Add(new ToolStripSeparator());

        if (hasSelection)
        {
            _canvasMenu.Items.Add(MenuItem("Xoá", (_, _) => Erase()));
            _canvasMenu.Items.Add(new ToolStripSeparator());
            _canvasMenu.Items.Add(MenuItem("Di chuyển", (_, _) => SetMode(EditMode.Move)));
            _canvasMenu.Items.Add(MenuItem("Sao chép", (_, _) => SetMode(EditMode.Copy)));
            _canvasMenu.Items.Add(MenuItem("Xoay", (_, _) => SetMode(EditMode.Rotate)));
            _canvasMenu.Items.Add(MenuItem("Tỷ lệ", (_, _) => SetMode(EditMode.Scale)));
            _canvasMenu.Items.Add(MenuItem("Đối xứng", (_, _) => SetMode(EditMode.Mirror)));
            _canvasMenu.Items.Add(MenuItem("Offset", (_, _) => SetMode(EditMode.Offset)));
            _canvasMenu.Items.Add(new ToolStripSeparator());
            _canvasMenu.Items.Add(MenuItem("Bỏ chọn", (_, _) => _canvas.ClearSelection()));
        }
        else
        {
            _canvasMenu.Items.Add(MenuItem("Chọn tất cả", (_, _) => _canvas.SelectAll()));
        }

        _canvasMenu.Items.Add(new ToolStripSeparator());
        _canvasMenu.Items.Add(MenuItem("Hoàn tác", (_, _) => { UndoAction(); UpdateStatus(); }));
        _canvasMenu.Items.Add(MenuItem("Làm lại", (_, _) => { RedoAction(); UpdateStatus(); }));
        _canvasMenu.Items.Add(new ToolStripSeparator());
        _canvasMenu.Items.Add(MenuItem("Zoom extents", (_, _) => _canvas.ZoomExtents()));
    }

    private static ToolStripMenuItem MenuItem(string text, EventHandler handler, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += handler;
        return item;
    }

    private void RebuildLayoutItems(ToolStripMenuItem menu)    {
        menu.DropDownItems.Clear();

        var names = _editor is null
            ? Array.Empty<string>()
            : OpenCad.Core.Layout.LayoutFactory.LayoutNames(_editor.Workspace.Document).ToArray();

        foreach (var name in names)
        {
            var captured = name;
            menu.DropDownItems.Add(Item($"In layout &{captured}…", Keys.None, (_, _) => PlotExistingLayout(captured)));
        }

        if (names.Length > 0) menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(Item("&Tạo layout A3 — 1:50", Keys.None, (_, _) => CreateLayout(50)));
        menu.DropDownItems.Add(Item("Tạo layout A3 — 1:100", Keys.None, (_, _) => CreateLayout(100)));
        menu.DropDownItems.Add(Item("Tạo layout A4 — 1:100", Keys.None, (_, _) => CreateLayout(100, a4: true)));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(Item("Thêm viewport lên tờ (VPORT)", Keys.None, (_, _) => SetMode(EditMode.PaperViewport)));
    }

    /// <summary>Tạo layout thật trong bản vẽ (lưu vào DWG), gồm khung, khung tên và viewport.</summary>
    private void CreateLayout(double scaleDenominator, bool a4 = false)
    {
        if (_editor is null) return;

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName),
            Project = "OpenCAD",
            Drafter = Environment.UserName,
            WidthMm = a4 ? 297 : 420,
            HeightMm = a4 ? 210 : 297,
            UnitsPerMm = scaleDenominator,
        };

        var baseName = $"{(a4 ? "A4" : "A3")}-1-{(int)scaleDenominator}";
        var name = baseName;
        var suffix = 2;

        while (OpenCad.Core.Layout.LayoutFactory.Find(_editor.Workspace.Document, name) is not null)
            name = $"{baseName}-{suffix++}";

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(
            _editor.Workspace.Document, name, template, _editor.Scene);

        if (layout is null)
        {
            MessageBox.Show(this, "Không tạo được layout.", "OpenCAD",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _editor.Workspace.MarkModified();
        UpdateTitle();
        _editor.Reload();
        RebuildLayoutTabs();

        Log($"Đã tạo layout '{name}' (1:{template.UnitsPerMm:0.##})");
        _statusInfo.Text = $"Layout {name} — 1:{template.UnitsPerMm:0.##}";
    }

    /// <summary>In một layout có sẵn trong bản vẽ.</summary>
    private void PlotExistingLayout(string layoutName)
    {
        if (_editor is null) return;

        var layout = OpenCad.Core.Layout.LayoutFactory.Find(_editor.Workspace.Document, layoutName);
        if (layout is null) return;

        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(_editor.Workspace.Document, layout);
        if (sheet is null)
        {
            MessageBox.Show(this, "Không đọc được layout.", "OpenCAD",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = $"In layout {layoutName}",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{layoutName}.pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            OpenCad.Core.Export.PdfExporter.ExportPaperSheet(sheet, dialog.FileName,
                new OpenCad.Core.Export.PdfExportOptions
                {
                    Title = layoutName,
                    FontPath = ResolvePdfFont(),
                    PlotStyle = _plotStyle,
                });

            Log($"Đã in layout {layoutName}: {dialog.FileName}");
            MessageBox.Show(this,
                $"Đã in layout '{layoutName}'" + Environment.NewLine + dialog.FileName,
                "OpenCAD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không in được layout:" + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = true;
        }
        item.Click += handler;
        return item;
    }


    private void WireEvents()
    {
        _canvas.PromptChanged += prompt =>
        {
            _command.SetPrompt(prompt);
            if (_canvas.AwaitingInput) _command.FocusInput();
        };

        _canvas.SelectionChanged += (_, _) => SyncSelection();
        _canvas.StateChanged += () => { UpdateTitle(); UpdateStatus(); };
        _canvas.CursorMoved += p => _statusCoords.Text = $"X {p.X:0.###}   Y {p.Y:0.###}";

        _canvas.Message += text =>
        {
            Log(text);
            _statusInfo.Text = text;
        };

        _layers.LayersChanged += () =>
        {
            // Tắt/đóng băng layer phải dựng lại scene, nếu không entity vẫn chọn được
            _editor.Reload();
            _canvas.Invalidate();
            RefreshLayerCombo();
            UpdateStatus();
        };
        _layers.LayerStateChanged += () =>
        {
            _editor.Reload();
            _canvas.Invalidate();
            UpdateStatus();
        };

        _layers.CurrentLayerChanged += layer =>
        {
            _canvas.CurrentLayer = layer;

            _layerCombo.SelectedItem = layer?.Name;

            UpdateStatus();
        };

        _props.LayerAssigned += () => { _canvas.Invalidate(); SyncSelection(); };

        // Đổi kiểu nét cho lựa chọn hiện tại
        _props.LineTypeAssigned += type =>
        {
            if (_canvas.Selection.Count == 0)
            {
                _statusHint = "Chọn đối tượng trước khi đổi kiểu nét.";
                UpdateStatus();
                return;
            }

            _editor.SetLineType(_canvas.Selection.ToList(), type);
            _canvas.Invalidate();
            _statusHint = $"Kiểu nét: {type.Name}";
            UpdateStatus();
        };

        _ribbon.CommandClicked += name => { RunCommand(name); _canvas.Focus(); };
        RefreshRecentMenu();

        _command.Suggest = OpenCad.Core.Commands.CommandRegistry.Suggest;
        _command.Submitted += RunCommand;
        _command.Cancelled += () => { _canvas.Cancel(); _canvas.Focus(); };

        _toggleOsnap.Click += (_, _) => { _canvas.ToggleSnap(); UpdateStatus(); };
        _toggleOrtho.Click += (_, _) => { _canvas.ToggleOrtho(); UpdateStatus(); };
        _togglePolar.Click += (_, _) => { _canvas.TogglePolar(); UpdateStatus(); };
        _toggleGrid.Click += (_, _) => { _canvas.ToggleGrid(); UpdateStatus(); };
        _toggleLwt.Click += (_, _) => { _canvas.ToggleLineWeights(); UpdateStatus(); };
    }

    private void ToggleHistory()
    {
        _historyList.Visible = !_historyList.Visible;
        if (_historyList.Visible) ScrollHistoryToEnd();
    }

    private void Log(string message)
    {
        _historyList.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (_historyList.Items.Count > 500) _historyList.Items.RemoveAt(0);
        if (_historyList.Visible) ScrollHistoryToEnd();
    }

    private void ScrollHistoryToEnd()
    {
        if (_historyList.Items.Count == 0) return;
        _historyList.TopIndex = Math.Max(0, _historyList.Items.Count - 1);
    }

    // ------------------------------------------------------------- DOCUMENT

    private void OnFirstLoad()
    {
        if (_initialPath is not null && File.Exists(_initialPath))
        {
            try
            {
                AttachWorkspace(CadWorkspace.Open(_initialPath));
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Không mở được file khởi động:" + Environment.NewLine + ex.Message,
                    "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        AttachWorkspace(CadWorkspace.New());
    }

    private void AttachWorkspace(CadWorkspace workspace)
    {
        _editor = new CadEditor(workspace);

        _canvas.Attach(_editor);

        // Font đi theo bản vẽ (lưu trong TextStyle "Standard")
        try { _canvas.ApplyDocumentFont(); } catch { }
        _layers.Attach(_editor);
        _props.Attach(_editor);

        _props.RefreshLayers();
        _layers.RefreshLayers();

        _canvas.CurrentLayer = _layers.CurrentLayer ?? _editor.Workspace.FindLayer("0");
        _layers.CurrentLayerChanged += _ => { };

        SyncSelection();
        UpdateTitle();
        UpdateStatus();

        _activeLayout = null;
        RebuildLayoutTabs();
        RefreshLayerCombo();
    }

    /// <summary>Mở một tệp theo đường dẫn (dùng cho menu "Mở gần đây").</summary>
    private void OpenPath(string path)
    {
        if (_editor is null || string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) return;

        if (!ConfirmDiscard()) return;

        UseWaitCursor = true;

        try
        {
            AttachWorkspace(CadWorkspace.Open(path));
            Log("Đã mở: " + path);
            RememberRecent(path);
        }
        catch (Exception ex)
        {
            Log("Không mở được '" + path + "': " + ex.Message);
            _statusHint = "Không mở được tệp";
        }
        finally
        {
            UseWaitCursor = false;
            UpdateStatus();
        }
    }

    /// <summary>Dựng lại danh sách "Mở gần đây" trong menu File.</summary>
    private void RefreshRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();

        var items = OpenCad.Core.RecentFiles.Load();

        if (items.Count == 0)
        {
            var empty = new ToolStripMenuItem("(chưa có tệp nào)") { Enabled = false };
            _recentMenu.DropDownItems.Add(empty);

            return;
        }

        foreach (var path in items)
        {
            var label = Path.GetFileName(path);
            var item = new ToolStripMenuItem(label) { ToolTipText = path };

            var target = path;
            item.Click += (_, _) => OpenPath(target);

            _recentMenu.DropDownItems.Add(item);
        }
    }

    /// <summary>Ghi nhớ tệp vừa mở/lưu và cập nhật menu.</summary>
    private void RememberRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try { OpenCad.Core.RecentFiles.Add(path); }
        catch { }

        RefreshRecentMenu();
    }

    private void NewDocument()
    {
        if (!ConfirmDiscard()) return;
        AttachWorkspace(CadWorkspace.New());
    }

    private void OpenDocument()
    {
        if (!ConfirmDiscard()) return;

        using var dialog = new OpenFileDialog
        {
            Title = "Mở bản vẽ",
            Filter = "Bản vẽ CAD (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf|Tất cả (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        UseWaitCursor = true;
        try
        {
            var workspace = CadWorkspace.Open(dialog.FileName);
            AttachWorkspace(workspace);

            if (workspace.LastWarnings.Count > 0)
            {
                var preview = string.Join(Environment.NewLine,
                    workspace.LastWarnings.Take(6).Select(w => "• " + w));
                MessageBox.Show(this,
                    $"Mở file xong nhưng có {workspace.LastWarnings.Count} cảnh báo:" +
                    Environment.NewLine + Environment.NewLine + preview,
                    "Cảnh báo khi đọc file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không mở được file:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private bool SaveDocument()
    {
        if (_editor is null) return false;

        if (_editor.Workspace.FilePath is null) return SaveDocumentAs();
        if (!ConfirmLossySave()) return false;

        var saved = WriteFile(_editor.Workspace.FilePath);

        // Lưu thật xong thì bỏ bản autosave — nếu không lần sau app lại mời "phục hồi"
        if (saved)
        {
            _editor.Workspace.ClearAutosave();
            RememberRecent(_editor.Workspace.FilePath);
        }

        return saved;
    }

    private bool SaveDocumentAs()
    {
        if (_editor is null) return false;

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu bản vẽ",
            Filter = "DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
            FileName = Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName) + ".dwg",
            AddExtension = true,
            DefaultExt = "dwg",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (!ConfirmLossySave()) return false;

        var savedAs = WriteFile(dialog.FileName);

        if (savedAs)
        {
            _editor.Workspace.ClearAutosave();
            RememberRecent(_editor.Workspace.FilePath);
        }

        return savedAs;
    }

    /// <summary>Bọc một ô CSV: có dấu phẩy/nháy/xuống dòng thì bọc trong nháy kép.</summary>
    private static string EscapeCsv(string? value)
    {
        var cell = value ?? string.Empty;

        if (cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return cell;

        return '"' + cell.Replace("\"", "\"\"") + '"';
    }

    /// <summary>
    /// Tệp font để nhúng vào PDF: ưu tiên font đang hiển thị trong bản vẽ,
    /// không tìm được thì dùng font hệ thống có dấu tiếng Việt.
    /// </summary>
    internal string? ResolvePdfFont()
    {
        try
        {
            var chosen = OpenCad.Core.Styles.FontLibrary.FileFor(_canvas.TextFontName);

            if (chosen is not null) return chosen;
        }
        catch { }

        return OpenCad.Core.Export.PdfExportOptions.FindSystemFont();
    }

    private void ExportPdf(double widthMm, double heightMm, double? scaleDenominator, bool monochrome = false)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Xuất PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName) + ".pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        UseWaitCursor = true;
        try
        {
            var options = new OpenCad.Core.Export.PdfExportOptions
            {
                PageWidthMm = widthMm,
                PageHeightMm = heightMm,
                Landscape = true,
                Title = _editor.Workspace.DisplayName,
                FitToPage = scaleDenominator is null,
                UnitsPerMm = scaleDenominator is { } d
                    ? OpenCad.Core.Export.PdfExportOptions.FromScaleDenominator(d)
                    : null,
                FontPath = ResolvePdfFont(),
                Monochrome = monochrome,
                PlotStyle = _plotStyle,
            };

            var result = OpenCad.Core.Export.PdfExporter.Export(
                _editor.Scene, dialog.FileName, options,
                entity => entity.Layer?.IsOn ?? true);

            var scaleText = $"1:{result.UnitsPerMm:0.##}";

            Log($"Đã xuất PDF: {dialog.FileName} (tỷ lệ {scaleText})");
            MessageBox.Show(this,
                "Đã xuất PDF:" + Environment.NewLine + dialog.FileName +
                Environment.NewLine + Environment.NewLine + $"Tỷ lệ in: {scaleText}",
                "OpenCAD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không xuất được PDF:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>In một tờ có khung tên: khung viền, viewport đúng tỷ lệ, khung tên.</summary>
    private void PlotSheet(double scaleDenominator)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "In tờ",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName) + "-to.pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        UseWaitCursor = true;
        try
        {
            var template = new OpenCad.Core.Layout.SheetTemplateOptions
            {
                Title = Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName),
                Project = "OpenCAD",
                Drafter = Environment.UserName,
                WidthMm = 420,
                HeightMm = 297,
                UnitsPerMm = scaleDenominator,
            };

            var sheet = OpenCad.Core.Layout.PaperSheetBuilder.Build(_editor.Scene, template);

            var options = new OpenCad.Core.Export.PdfExportOptions
            {
                Title = template.Title,
                FontPath = ResolvePdfFont(),
            };

            OpenCad.Core.Export.PdfExporter.ExportPaperSheet(sheet, dialog.FileName, options);

            Log($"Đã in tờ: {dialog.FileName} (1:{template.UnitsPerMm:0.##})");
            MessageBox.Show(this,
                "Đã in tờ:" + Environment.NewLine + dialog.FileName +
                Environment.NewLine + Environment.NewLine + $"Tỷ lệ 1:{template.UnitsPerMm:0.##}",
                "OpenCAD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không in được tờ:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>In nhiều bản vẽ vào một file PDF — mỗi file một tờ.</summary>
    private void BatchPlot()
    {
        using var open = new OpenFileDialog
        {
            Title = "Chọn các bản vẽ cần in",
            Filter = "Bản vẽ CAD (*.dwg;*.dxf)|*.dwg;*.dxf|Tất cả (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (open.ShowDialog(this) != DialogResult.OK || open.FileNames.Length == 0) return;

        using var save = new SaveFileDialog
        {
            Title = "Lưu bộ hồ sơ PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = "bo-ho-so.pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true,
        };

        if (save.ShowDialog(this) != DialogResult.OK) return;

        UseWaitCursor = true;
        try
        {
            var fontPath = ResolvePdfFont();
            var sheets = new List<(CadScene Scene, OpenCad.Core.Export.PdfExportOptions Options)>();

            foreach (var file in open.FileNames)
            {
                var workspace = CadWorkspace.Open(file);
                var scene = new CadScene();
                scene.Build(workspace.Document);

                sheets.Add((scene, new OpenCad.Core.Export.PdfExportOptions
                {
                    PageWidthMm = 420,
                    PageHeightMm = 297,
                    Title = Path.GetFileName(file),
                    FitToPage = true,
                    FontPath = fontPath,
                }));
            }

            var baseOptions = new OpenCad.Core.Export.PdfExportOptions
            {
                Title = "Bộ hồ sơ",
                FontPath = fontPath,
            };

            var results = OpenCad.Core.Export.PdfExporter.ExportSheets(
                sheets, save.FileName, baseOptions,
                entity => entity.Layer?.IsOn ?? true);

            Log($"Đã in bộ hồ sơ: {results.Count} tờ → {save.FileName}");

            MessageBox.Show(this,
                $"Đã in {results.Count} tờ vào:" + Environment.NewLine + save.FileName,
                "OpenCAD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không in được bộ hồ sơ:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>
    /// Cảnh báo nếu lưu sẽ làm mất entity mà bộ ghi DWG không hỗ trợ.
    /// Trả về true nếu người dùng đồng ý tiếp tục.
    /// </summary>
    private bool ConfirmLossySave()
    {
        if (_editor is null) return true;

        var risks = _editor.Workspace.LossySaveRisks();
        if (risks.Count == 0) return true;

        var detail = string.Join(Environment.NewLine,
            risks.Select(r => $"  • {r.Type}: {r.Count} đối tượng"));

        var answer = MessageBox.Show(this,
            "Các đối tượng sau KHÔNG lưu được ở định dạng DWG:"
            + Environment.NewLine + Environment.NewLine + detail
            + Environment.NewLine + Environment.NewLine
            + "Yes    = chuyển sang dạng tương đương (giữ nội dung) rồi lưu"
            + Environment.NewLine
            + "No     = lưu và CHẤP NHẬN MẤT"
            + Environment.NewLine
            + "Cancel = không lưu",
            "Cảnh báo mất dữ liệu", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);

        if (answer == DialogResult.Cancel) return false;

        if (answer == DialogResult.Yes)
        {
            var converted = _editor.Workspace.ConvertUnsupportedForSave();

            if (converted > 0)
            {
                Log($"Đã chuyển {converted} đối tượng không lưu được sang dạng tương đương.");
                _editor.Reload();
            }
        }

        return true;
    }

    private bool WriteFile(string path)    {
        UseWaitCursor = true;
        try
        {
            _editor.Workspace.Save(path);
            _editor.MarkSaved();
            UpdateTitle();
            UpdateStatus();
            Log($"Đã lưu: {path}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không lưu được file:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    // -------------------------------------------------------------- COMMAND

    // ------------------------------------------- ĐƯỜNG ĐI KHÔNG HỘP THOẠI
    // Tách "hành động" khỏi "hộp thoại" để kiểm thử được toàn bộ lệnh (nợ từ vòng 83).

    /// <summary>Lưu ra đường dẫn cụ thể, không hỏi. Trả về true nếu ghi được.</summary>
    internal bool SaveToPathForTest(string path)
    {
        if (_editor is null || string.IsNullOrWhiteSpace(path)) return false;

        try { return WriteFile(path); }
        catch { return false; }
    }

    /// <summary>Mở tệp theo đường dẫn, không hộp thoại.</summary>
    internal bool OpenPathForTest(string path)
    {
        OpenPath(path);

        return _editor is not null &&
               string.Equals(_editor.Workspace.FilePath, path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tạo bản vẽ mới, không hỏi lưu.</summary>
    internal void NewDocumentForTest()
    {
        AttachWorkspace(CadWorkspace.New());
    }

    /// <summary>Xuất PDF ra đường dẫn cụ thể, không hộp thoại.</summary>
    internal void ExportPdfToPathForTest(string path, double widthMm = 420, double heightMm = 297)
    {
        if (_editor is null || string.IsNullOrWhiteSpace(path)) return;

        var options = new OpenCad.Core.Export.PdfExportOptions
        {
            PageWidthMm = widthMm,
            PageHeightMm = heightMm,
            Landscape = true,
            Title = _editor.Workspace.DisplayName,
            FitToPage = true,
            FontPath = ResolvePdfFont(),
            Monochrome = true,
            PlotStyle = _plotStyle,
        };

        OpenCad.Core.Export.PdfExporter.Export(
            _editor.Scene, path, options, entity => entity.Layer?.IsOn ?? true);

        Log("Đã xuất PDF (kiểm thử): " + path);
    }

    /// <summary>Chạy dọn định nghĩa mồ côi, không hộp thoại báo kết quả.</summary>
    internal int PurgeForTest()
    {
        if (_editor is null) return 0;

        var plan = OpenCad.Core.Audit.DrawingPurge.Inspect(
            _editor.Workspace.Document, _canvas.CurrentLayer?.Name);

        if (plan.Total == 0) return 0;

        OpenCad.Core.Audit.DrawingPurge.Apply(_editor.Workspace.Document, plan);

        _editor.Reload();
        _layers.RefreshLayers();

        return plan.Total;
    }

    /// <summary>Mọi phím tắt khai báo trong menu — dùng cho kiểm thử.</summary>
    internal List<(string? Text, Keys Shortcut)> MenuShortcutsForTest()
    {
        var result = new List<(string?, Keys)>();

        void Walk(ToolStripItem item)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                if (menuItem.ShortcutKeys != Keys.None)
                    result.Add((menuItem.Text, menuItem.ShortcutKeys));

                foreach (ToolStripItem child in menuItem.DropDownItems) Walk(child);
            }
            else if (item is ToolStripDropDownItem dropDown)
            {
                foreach (ToolStripItem child in dropDown.DropDownItems) Walk(child);
            }
        }

        if (MainMenuStrip is not null)
        {
            foreach (ToolStripItem item in MainMenuStrip.Items) Walk(item);
        }

        return result;
    }

    /// <summary>Gửi một tổ hợp phím qua đúng đường xử lý của app — dùng cho kiểm thử.</summary>
    internal bool ProcessKeyForTest(Keys keyData)
    {
        var message = Message.Create(Handle, 0, IntPtr.Zero, IntPtr.Zero);

        return ProcessCmdKey(ref message, keyData);
    }

    /// <summary>Phím tắt của mục Undo/Redo — dùng cho kiểm thử.</summary>
    internal (Keys Undo, Keys Redo) ShortcutsForTest => (_undoItem.ShortcutKeys, _redoItem.ShortcutKeys);

    /// <summary>Toàn bộ nội dung cửa sổ lịch sử — dùng cho kiểm thử.</summary>
    internal string HistoryTextForTest =>
        string.Join("\n", _historyList.Items.Cast<object>().Select(o => o.ToString()));

    /// <summary>Mở một layout theo tên — dùng cho kiểm thử.</summary>
    internal void OpenLayoutForTest(string name) => SelectLayoutTab(name);

    /// <summary>Canvas bên trong — dùng cho kiểm thử.</summary>
    internal CadCanvas CanvasForTest => _canvas;

    /// <summary>Editor bên trong — dùng cho kiểm thử.</summary>
    internal CadEditor? EditorForTest => _editor;

    /// <summary>Khởi tạo bản vẽ như khi cửa sổ hiện lần đầu — dùng cho kiểm thử.</summary>
    internal void InitializeForTest() => OnFirstLoad();

    // ------------------------------------------------------------- AUTOSAVE
    private readonly System.Windows.Forms.Timer _autosaveTimer = new() { Interval = 120_000 };

    /// <summary>Bật autosave định kỳ (mặc định bật, 2 phút một lần khi có thay đổi).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AutosaveEnabled { get; set; } = true;

    /// <summary>Ghi autosave ngay nếu bản vẽ đã thay đổi. Trả về đường dẫn đã ghi, hoặc null.</summary>
    internal string? RunAutosaveNow()
    {
        if (!AutosaveEnabled || _editor is null) return null;
        if (!_editor.Workspace.IsModified) return null;

        var path = _editor.Workspace.WriteAutosave();

        if (path is not null) Log("Autosave: " + path);

        return path;
    }

    /// <summary>Kiểm tra và mời phục hồi bản autosave còn sót (sau crash).</summary>
    private void OfferAutosaveRecovery()
    {
        var files = CadWorkspace.FindAutosaves();

        if (files.Count == 0) return;

        var newest = files[0];
        var when = File.GetLastWriteTime(newest);

        var answer = MessageBox.Show(this,
            "Có bản autosave từ lần chạy trước (có thể do ứng dụng bị đóng đột ngột):"
            + Environment.NewLine + Environment.NewLine
            + "   " + Path.GetFileName(newest) + Environment.NewLine
            + "   lúc " + when.ToString("HH:mm dd/MM/yyyy") + Environment.NewLine + Environment.NewLine
            + "Mở bản autosave này?",
            "Phục hồi bản vẽ", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (answer != DialogResult.Yes) return;

        try
        {
            AttachWorkspace(CadWorkspace.Open(newest));
            Log("Đã phục hồi từ autosave: " + newest);
            _statusHint = "Đã phục hồi bản autosave — hãy Lưu thành tệp mới (Ctrl+Shift+S).";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không mở được autosave: " + ex.Message,
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Báo trạng thái autosave, đồng thời ghi ngay một bản (lệnh AUTOSAVE).</summary>
    /// <summary>
    /// LAYDEL kiểu AutoCAD: xoá layer đang chọn (trong panel Layer) cùng mọi đối tượng trên đó.
    /// </summary>
    private void DeleteSelectedLayerWithContents()
    {
        if (_editor is null) return;

        var name = _layers.SelectedLayerName;

        if (string.IsNullOrWhiteSpace(name))
        {
            Log("LAYDEL: chọn một layer trong panel Layer trước.");
            _statusHint = "LAYDEL: chưa chọn layer";
            UpdateStatus();
            return;
        }

        var layer = _editor.Workspace.FindLayer(name);

        if (layer is null)
        {
            Log("LAYDEL: không tìm thấy layer '" + name + "'.");
            return;
        }

        var removed = _editor.DeleteLayerAndContents(layer, _editor.Workspace.Document.Layers.FirstOrDefault()?.Name);

        if (removed < 0)
        {
            Log($"LAYDEL: không xoá được layer '{name}' (layer 0 hoặc đang là layer hiện hành).");
            _statusHint = "LAYDEL: không xoá được layer này";
        }
        else
        {
            Log($"LAYDEL: đã xoá layer '{name}' và {removed} đối tượng.");
            _statusHint = $"Đã xoá layer '{name}' + {removed} đối tượng";
        }

        _layers.RefreshLayers();
        _canvas.ClearSelection();
        _canvas.Invalidate();
        UpdateStatus();
    }

    /// <summary>Xoá layout đang mở (LAYOUTDEL). Không xoá layout cuối cùng.</summary>
    private void DeleteCurrentLayout()
    {
        if (_editor is null) return;

        var name = _canvas.ActiveLayoutName;

        if (string.IsNullOrEmpty(name))
        {
            Log("LAYOUTDEL: đang ở model space — mở tab layout cần xoá trước.");
            _statusHint = "LAYOUTDEL: cần mở một layout trước";
            UpdateStatus();
            return;
        }

        var error = OpenCad.Core.Layout.LayoutFactory.Delete(_editor.Workspace.Document, name);

        if (error is not null)
        {
            Log("LAYOUTDEL: " + error);
            _statusHint = error;
            UpdateStatus();
            return;
        }

        Log($"LAYOUTDEL: đã xoá '{name}'");

        _canvas.ShowPaper(null);
        RebuildLayoutTabs();
        _canvas.Invalidate();
        _statusHint = $"Đã xoá layout '{name}'";
        UpdateStatus();
    }

    private void ReportAutosave()
    {
        var path = RunAutosaveNow();

        Log("AUTOSAVE");
        Log("   Trạng thái : " + (AutosaveEnabled ? "bật" : "tắt"));
        Log("   Chu kỳ     : 2 phút (khi có thay đổi)");
        Log("   Vị trí     : " + (_editor?.Workspace.AutosavePath ?? "—"));
        Log("   Lần cuối   : " + (_editor?.Workspace.LastAutosave?.ToString("HH:mm:ss") ?? "chưa"));
        Log("   Vừa ghi    : " + (path ?? "không (không có thay đổi)"));

        _statusHint = path is null ? "AUTOSAVE: không có thay đổi để ghi" : "AUTOSAVE: đã ghi";
        UpdateStatus();
    }

    /// <summary>Khởi động autosave và mời phục hồi bản còn sót.</summary>
    private void StartAutosave()
    {
        _autosaveTimer.Tick += (_, _) => RunAutosaveNow();
        _autosaveTimer.Start();

        OfferAutosaveRecovery();
    }

    /// <summary>Chạy một lệnh như người dùng gõ — dùng cho kiểm thử.</summary>
    internal void RunCommandForTest(string raw) => RunCommand(raw);

    /// <summary>Huỷ lệnh đang chạy — dùng cho kiểm thử.</summary>
    internal void CancelForTest() => _canvas.Cancel();

    private void RunCommand(string raw)
    {
        var text = raw.Trim();

        // 1. Đang chờ dữ liệu (text / hệ số / góc) hoặc Enter kết thúc công cụ
        if (_canvas.ProvideInput(text))
        {
            FocusAfterCommand();
            UpdateStatus();
            return;
        }

        if (text.Length == 0)
        {
            FocusAfterCommand();
            return;
        }

        var command = text.ToUpperInvariant();
        Log($"Command: {text}");
        _statusHint = null;

        if (command == "LANG" || command.StartsWith("LANG ") || command == "LANGUAGE" || command.StartsWith("LANGUAGE "))
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                var lang = parts[1];
                if (I18n.SetLanguage(lang))
                {
                    Log(I18n.CurrentLanguage == AppLanguage.English
                        ? "Language switched to English."
                        : "Đã đổi ngôn ngữ sang Tiếng Việt.");
                }
                else
                {
                    Log("Usage: LANG [EN|VI]");
                }
            }
            else
            {
                ToggleLanguage();
                Log(I18n.CurrentLanguage == AppLanguage.English
                    ? "Language switched to English."
                    : "Đã đổi ngôn ngữ sang Tiếng Việt.");
            }
            FocusAfterCommand();
            UpdateStatus();
            return;
        }

        var handled = true;

        // Lệnh có tham số: DSTYLE <tên>
        var spaceAt = command.IndexOf(' ');
        if (spaceAt > 0)
        {
            var head = command[..spaceAt];
            var tail = text[(spaceAt + 1)..].Trim();

            if (head is "DIMSTYLE" or "DST")
            {
                HandleDimStyle(tail);
                FocusAfterCommand();
                UpdateStatus();
                return;
            }
        }

        // Lệnh có tham số dạng "TÊN tham số thứ nhất tham số thứ hai…":
        // switch bên dưới khớp TÊN LỆNH, còn tham số đọc từ biến `text`.
        // Trước đây switch khớp cả chuỗi nên "ATTOUT <tệp>", "WBLOCK <tệp>",
        // "IMPORT <tệp>", "PATLOAD <tệp>", "TEXTANGLE 45"… KHÔNG BAO GIỜ chạy được.
        var dispatch = command;

        if (spaceAt > 0)
        {
            var head = command[..spaceAt];

            if (OpenCad.Core.Commands.CommandRegistry.Find(head) is not null)
                dispatch = head;
        }

        switch (dispatch)
        {
            case "L": case "LINE": SetMode(EditMode.Line); break;
            case "PL": case "PLINE": SetMode(EditMode.Polyline); break;
            case "REC": case "RECTANG": SetMode(EditMode.Rectangle); break;
            case "C": case "CIRCLE": SetMode(EditMode.Circle); break;
            case "A": case "ARC": SetMode(EditMode.Arc); break;
            case "T": case "TEXT": case "DT": case "MTEXT": SetMode(EditMode.Text); break;
            case "H": case "HATCH": SetMode(EditMode.Hatch); break;

            case "M": case "MOVE": SetMode(EditMode.Move); break;
            case "CO": case "CP": case "COPY": SetMode(EditMode.Copy); break;
            case "RO": case "ROTATE": SetMode(EditMode.Rotate); break;
            case "SC": case "SCALE": SetMode(EditMode.Scale); break;
            case "MI": case "MIRROR": SetMode(EditMode.Mirror); break;
            case "O": case "OFFSET": SetMode(EditMode.Offset); break;
            case "TR": case "TRIM": SetMode(EditMode.Trim); break;
            case "EX": case "EXTEND": SetMode(EditMode.Extend); break;
            case "F": case "FILLET": SetMode(EditMode.Fillet); break;
            case "CHA": case "CHAMFER": SetMode(EditMode.Chamfer); break;
            case "DIM": case "DLI": case "DIMLINEAR": SetMode(EditMode.Dimension); break;
            case "DAL": case "DIMALIGNED": SetMode(EditMode.Dimension); break;
            case "DRA": case "DIMRAD": case "DIMRADIUS": SetMode(EditMode.DimensionRadius); break;
            case "DDI": case "DIMDIA": case "DIMDIAMETER": SetMode(EditMode.DimensionDiameter); break;
            case "DAN": case "DIMANG": case "DIMANGULAR": SetMode(EditMode.DimensionAngular); break;
            case "DOR": case "DIMORD": case "DIMORDINATE": SetMode(EditMode.DimensionOrdinate); break;
            case "DIMSTYLE": case "DST": HandleDimStyle(string.Empty); break;
            case "PTEXT": case "PT": SetMode(EditMode.PaperText); break;
            case "VPORT": case "VP": SetMode(EditMode.PaperViewport); break;
            case "AR": case "ARRAY": SetMode(EditMode.Array); break;
            case "ARP": case "ARRAYPOLAR": SetMode(EditMode.ArrayPolar); break;
            case "S": case "STRETCH": SetMode(EditMode.Stretch); break;
            case "ATT": case "ATTEDIT": SetMode(EditMode.AttributeEdit); break;
            case "ATTDEF": case "AD": SetMode(EditMode.AttributeDefine); break;
            case "DIV": case "DIVIDE": SetMode(EditMode.Divide); break;
            case "ME": case "MEASURE": SetMode(EditMode.Measure); break;
            case "SL": case "SHEETLINE": SetMode(EditMode.PaperLine); break;
            case "XL": case "XLINE": SetMode(EditMode.XLine); break;
            case "RAY": SetMode(EditMode.Ray); break;
            case "EL": case "ELLIPSE": SetMode(EditMode.Ellipse); break;
            case "POL": case "POLYGON": SetMode(EditMode.Polygon); break;
            case "DI": case "DIST": SetMode(EditMode.Dist); break;
            case "AA": case "AREA": SetMode(EditMode.Area); break;
            case "I": case "INSERT": SetMode(EditMode.Insert); break;
            case "B": case "BLOCK": SetMode(EditMode.BlockCreate); break;
            case "ED": case "DDEDIT": case "TEDIT": SetMode(EditMode.EditTextPick); break;
            case "BLOCKS": case "BL": ListBlocks(); break;
            case "UNSUPPORTED": case "SKIP": ListUnsupported(); break;
            case "HELP": case "?": ListCommands(); break;
            case "HE": case "HATCHEDIT": _canvas.BeginHatchEdit(); break;
            case "COPYCLIP": _canvas.CopySelectionToClipboard(); break;
            case "LAYISO": IsolateSelectedLayers(); break;
            case "DIMTEDIT": _canvas.BeginDimensionTextEdit(); break;
            case "DIAG": case "DIAGNOSTICS": ShowDiagnostics(); break;
            case "AUTOSAVE": ReportAutosave(); break;
            case "OOPS": _canvas.RestoreLastErased(); SyncSelection(); break;
            case "X": case "EXPLODE": _canvas.ExplodeSelection(); SyncSelection(); break;
            case "J": case "JOIN": _canvas.JoinSelection(); SyncSelection(); break;
            case "SELECTUNDER": case "SELALLD": _canvas.SelectUnderCursor(); SyncSelection(); break;

            case "MLD":
            case "MLEADER":
                SetMode(EditMode.MLeader);
                break;

            case "IMPORT":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');
                var rest = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

                // Dạng: IMPORT <đường dẫn> [tên block]
                string importPath;
                string? importName = null;

                if (rest.StartsWith("\"", StringComparison.Ordinal))
                {
                    var close = rest.IndexOf('"', 1);

                    if (close < 0)
                    {
                        Log("IMPORT: thiếu dấu nháy đóng.");
                        break;
                    }

                    importPath = rest[1..close].Trim();
                    var tail = rest[(close + 1)..].Trim();

                    if (tail.Length > 0) importName = tail;
                }
                else
                {
                    var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                    importPath = parts.Length > 0 ? parts[0] : string.Empty;

                    if (parts.Length > 1) importName = parts[1].Trim();
                }

                if (importPath.Length == 0)
                {
                    Log("IMPORT: cần đường dẫn tệp. Ví dụ: IMPORT samples/chi-tiet.dwg CHI_TIET");
                    _statusHint = "IMPORT: thiếu đường dẫn";
                    UpdateStatus();
                    break;
                }

                if (!File.Exists(importPath))
                {
                    Log("IMPORT: không tìm thấy '" + importPath + "'.");
                    _statusHint = "IMPORT: không thấy tệp";
                    UpdateStatus();
                    break;
                }

                ACadSharp.Tables.BlockRecord? imported;

                try
                {
                    imported = OpenCad.Core.Editing.ImportOperation.Import(
                        _editor.Workspace, importPath, importName);
                }
                catch (Exception ex)
                {
                    Log("IMPORT: lỗi — " + ex.Message);
                    _statusHint = "IMPORT: lỗi khi nạp";
                    UpdateStatus();
                    break;
                }

                if (imported is null)
                {
                    Log("IMPORT: không nạp được nội dung nào từ '" + importPath + "'.");
                    _statusHint = "IMPORT: tệp rỗng hoặc lỗi";
                    UpdateStatus();
                    break;
                }

                Log($"IMPORT: đã tạo block '{imported.Name}' với {imported.Entities.Count} đối tượng.");
                Log("   Dùng INSERT " + imported.Name + " để đặt vào bản vẽ.");

                _statusHint = $"Đã nạp block '{imported.Name}' ({imported.Entities.Count} đối tượng)";
                _layers.RefreshLayers();
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "WBLOCK":
            {
                if (_editor is null) break;

                var selection = _canvas.Selection.ToList();

                if (selection.Count == 0)
                {
                    Log("WBLOCK: chọn đối tượng cần xuất trước.");
                    _statusHint = "WBLOCK: chưa chọn đối tượng";
                    UpdateStatus();
                    break;
                }

                var spaceIndex = text.IndexOf(' ');
                var targetPath = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim().Trim('"') : string.Empty;

                if (targetPath.Length == 0)
                {
                    targetPath = Path.Combine(
                        Path.GetDirectoryName(_editor.Workspace.FilePath) ?? ".",
                        Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName) + "-trich.dwg");
                }

                if (!targetPath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) &&
                    !targetPath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                    targetPath += ".dwg";

                int written;

                try
                {
                    written = OpenCad.Core.Editing.WBlockOperation.Write(selection, targetPath);
                }
                catch (Exception ex)
                {
                    Log("WBLOCK: lỗi — " + ex.Message);
                    _statusHint = "WBLOCK: không ghi được tệp";
                    UpdateStatus();
                    break;
                }

                Log(written == 0
                    ? "WBLOCK: không có gì để ghi."
                    : $"WBLOCK: đã ghi {written} đối tượng → {targetPath}");

                _statusHint = written == 0 ? "WBLOCK: không ghi được" : $"Đã xuất {written} đối tượng";
                UpdateStatus();
                break;
            }

            case "PLOTSTYLE":
            {
                var spaceIndex = text.IndexOf(' ');
                var stylePath = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim().Trim('"') : string.Empty;

                if (stylePath.Length == 0)
                {
                    Log("PLOTSTYLE: cần đường dẫn tệp. Ví dụ: PLOTSTYLE samples/kieu-in.txt");
                    _statusHint = "PLOTSTYLE: thiếu đường dẫn";
                    UpdateStatus();
                    break;
                }

                var table = OpenCad.Core.Export.PlotStyleTable.LoadFile(stylePath);

                if (table is null)
                {
                    Log("PLOTSTYLE: không đọc được '" + stylePath + "'.");
                    _statusHint = "PLOTSTYLE: lỗi đọc tệp";
                    UpdateStatus();
                    break;
                }

                _plotStyle = table;

                Log($"PLOTSTYLE: {table.Count} lớp, đơn sắc = {table.Monochrome}, mặc định {table.DefaultWeightMm:0.##} mm");
                Log("   Lớp: " + string.Join(", ", table.LayerNames));

                _statusHint = $"Kiểu in: {table.Count} lớp từ {Path.GetFileName(stylePath)}";
                UpdateStatus();
                break;
            }

            case "FONT":
            case "TEXTSTYLE":
            {
                var spaceIndex = text.IndexOf(' ');

                if (spaceIndex < 0 || spaceIndex + 1 >= text.Length)
                {
                    Log("TEXTSTYLE: font đang dùng = " + _canvas.TextFontName);
                    Log("   Dùng: TEXTSTYLE <tên font>, ví dụ TEXTSTYLE Times New Roman");
                    Log("   Font có trên máy: " + string.Join(", ",
                        FontFamily.Families.Select(f => f.Name).Take(12)));

                    _statusHint = "TEXTSTYLE: nhập tên font";
                    UpdateStatus();
                    break;
                }

                var fontName = text[(spaceIndex + 1)..].Trim().Trim('"');
                var changed = _canvas.SetTextFont(fontName);

                Log(changed
                    ? $"TEXTSTYLE: đã đổi sang '{fontName}'"
                    : $"TEXTSTYLE: không có font '{fontName}'");

                _statusHint = changed ? "Font: " + _canvas.TextFontName : "Không có font này";
                UpdateStatus();
                break;
            }

            case "ATTSYNC":
            {
                if (_editor is null) break;

                // Block lấy từ insert đang chọn, hoặc theo tên trong tham số
                var spaceIndex = text.IndexOf(' ');
                var blockName = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

                ACadSharp.Tables.BlockRecord? block = null;

                if (blockName.Length > 0)
                {
                    block = _editor.Workspace.Document.BlockRecords
                        .FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    block = _canvas.Selection.OfType<ACadSharp.Entities.Insert>().FirstOrDefault()?.Block;
                }

                if (block is null)
                {
                    Log("ATTSYNC: chọn một insert, hoặc gõ ATTSYNC <tên block>.");
                    _statusHint = "ATTSYNC: chưa xác định được block";
                    UpdateStatus();
                    break;
                }

                var addedAttributes = _editor.SyncAttributes(block);

                Log(addedAttributes == 0
                    ? $"ATTSYNC: block '{block.Name}' đã đồng bộ sẵn."
                    : $"ATTSYNC: thêm {addedAttributes} attribute cho block '{block.Name}'.");

                _statusHint = addedAttributes == 0
                    ? $"ATTSYNC: '{block.Name}' không cần đồng bộ"
                    : $"Đã đồng bộ {addedAttributes} attribute";

                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "PATLOAD":
            case "HATCHPATLOAD":
            {
                var spaceIndex = text.IndexOf(' ');
                var patternPath = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim().Trim('"') : string.Empty;

                if (patternPath.Length == 0)
                {
                    Log("PATLOAD: cần đường dẫn file .pat. Ví dụ: PATLOAD C:/AutoCAD/Support/acad.pat");
                    Log("   Hiện có " + OpenCad.Core.Styles.HatchPatternLibrary.Names.Count + " mẫu: "
                        + string.Join(", ", OpenCad.Core.Styles.HatchPatternLibrary.Names));
                    _statusHint = "PATLOAD: thiếu đường dẫn file .pat";
                    UpdateStatus();
                    break;
                }

                if (!File.Exists(patternPath))
                {
                    Log("PATLOAD: không tìm thấy '" + patternPath + "'.");
                    _statusHint = "PATLOAD: không thấy tệp";
                    UpdateStatus();
                    break;
                }

                int loaded;

                try { loaded = OpenCad.Core.Styles.HatchPatternLibrary.LoadFile(patternPath); }
                catch (Exception ex)
                {
                    Log("PATLOAD: lỗi đọc tệp — " + ex.Message);
                    _statusHint = "PATLOAD: lỗi đọc tệp";
                    UpdateStatus();
                    break;
                }

                Log($"PATLOAD: nạp {loaded} mẫu từ {Path.GetFileName(patternPath)}");
                Log("   Tổng số mẫu hiện có: " + OpenCad.Core.Styles.HatchPatternLibrary.Names.Count);

                _statusHint = loaded == 0
                    ? "PATLOAD: không đọc được mẫu nào"
                    : $"Đã nạp {loaded} mẫu tô";

                UpdateStatus();
                break;
            }

            case "ATTOUT":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');
                var targetPath = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim().Trim('"') : string.Empty;

                if (targetPath.Length == 0)
                {
                    targetPath = Path.Combine(
                        Path.GetDirectoryName(_editor.Workspace.FilePath) ?? ".",
                        Path.GetFileNameWithoutExtension(_editor.Workspace.DisplayName) + "-thong-ke.csv");
                }

                if (!targetPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    targetPath += ".csv";

                var (headers, rows) = _editor.ExtractAttributes();

                if (rows.Count == 0)
                {
                    Log("ATTOUT: không có block insert nào có attribute.");
                    _statusHint = "ATTOUT: không có attribute";
                    UpdateStatus();
                    break;
                }

                try
                {
                    var builder = new System.Text.StringBuilder();
                    builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

                    foreach (var row in rows)
                    {
                        builder.AppendLine(string.Join(",", headers.Select(h =>
                            EscapeCsv(row.TryGetValue(h, out var v) ? v : string.Empty))));
                    }

                    // UTF-8 BOM để Excel mở đúng tiếng Việt
                    File.WriteAllText(targetPath, builder.ToString(),
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }
                catch (Exception ex)
                {
                    Log("ATTOUT: lỗi ghi tệp — " + ex.Message);
                    _statusHint = "ATTOUT: không ghi được";
                    UpdateStatus();
                    break;
                }

                Log($"ATTOUT: đã xuất {rows.Count} dòng, {headers.Count} cột → {targetPath}");
                _statusHint = $"Đã xuất thống kê {rows.Count} dòng";
                UpdateStatus();
                break;
            }

            case "LIST":
            case "LS":
            {
                if (_editor is null) break;

                var target = _canvas.Selection.Count > 0
                    ? _canvas.Selection.ToList()
                    : _editor.Workspace.Document.Entities.ToList();

                var described = _editor.Describe(target);

                if (described.Count == 0)
                {
                    Log("LIST: bản vẽ trống.");
                    break;
                }

                foreach (var line in described.Take(60)) Log(line);

                if (described.Count > 60) Log($"   … còn {described.Count - 60} dòng nữa.");

                _statusHint = $"LIST: {target.Count} đối tượng";
                UpdateStatus();
                break;
            }

            case "TEXTANGLE":
            case "TORIENT":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');
                var value = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

                if (value.Length == 0)
                {
                    Log("TEXTANGLE: đặt góc xoay cho chữ đang chọn. Ví dụ: TEXTANGLE 45");
                    Log("   (hoặc sửa ô 'Xoay' trong bảng Thuộc tính)");
                    _statusHint = "TEXTANGLE: nhập góc (độ)";
                    UpdateStatus();
                    break;
                }

                if (!double.TryParse(value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var degrees))
                {
                    Log("TEXTANGLE: '" + value + "' không phải số.");
                    _statusHint = "TEXTANGLE: góc không hợp lệ";
                    UpdateStatus();
                    break;
                }

                var rotated = _editor.SetTextRotation(_canvas.Selection, degrees);

                Log(rotated == 0
                    ? "TEXTANGLE: chưa chọn chữ nào (hoặc góc không đổi)."
                    : $"TEXTANGLE: xoay {rotated} chữ thành {degrees:0.##}°");

                _statusHint = rotated == 0 ? "Không có chữ để xoay" : $"Đã xoay {rotated} chữ";
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "OVERKILLMERGE":
            case "OVM":
            {
                if (_editor is null) break;

                var merged = _editor.MergeOverlappingLines();

                Log(merged == 0
                    ? "OVERKILL: không có nét chồng/nối tiếp nào để gộp."
                    : $"OVERKILL: gộp nét chồng — bỏ {merged} đoạn thừa.");

                _statusHint = merged == 0 ? "Không có nét chồng" : $"Đã gộp, bỏ {merged} đoạn";
                _canvas.ClearSelection();
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "OVERKILL":
            {
                if (_editor is null) break;

                var removed = _editor.RemoveDuplicates();

                Log(removed == 0
                    ? "OVERKILL: không có đối tượng trùng."
                    : $"OVERKILL: đã xoá {removed} đối tượng trùng.");

                _statusHint = removed == 0
                    ? "OVERKILL: không có gì trùng"
                    : $"Đã xoá {removed} đối tượng trùng";

                _layers.RefreshLayers();
                _canvas.ClearSelection();
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "TABLECELL":
            {
                var spaceIndex = text.IndexOf(' ');
                var argument = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

                _canvas.SetTableCellLabel(argument);
                SyncSelection();
                break;
            }

            case "INFO":
            {
                if (_editor is null) break;

                var doc = _editor.Workspace.Document;

                var byType = doc.Entities
                    .GroupBy(e => e.GetType().Name)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                Log("THỐNG KÊ BẢN VẼ: " + _editor.Workspace.DisplayName);
                Log($"   Entity   : {doc.Entities.Count()}");
                Log($"   Layer    : {doc.Layers.Count()}");
                Log($"   Block    : {doc.BlockRecords.Count()}");
                Log($"   Layout   : {OpenCad.Core.Layout.LayoutFactory.LayoutNames(doc).Count}");
                Log($"   Primitive: {_editor.Scene.PrimitiveCount}");

                var bounds = _editor.Scene.Bounds;

                Log($"   Vùng     : [{bounds.MinX:0.#}, {bounds.MinY:0.#} .. {bounds.MaxX:0.#}, {bounds.MaxY:0.#}]");
                Log("   Theo loại:");

                foreach (var group in byType) Log($"      {group.Key,-22} {group.Count()}");

                _statusHint = $"INFO: {doc.Entities.Count()} entity, {doc.Layers.Count()} layer, {byType.Count} loại";
                UpdateStatus();
                break;
            }

            case "SCR":
            case "SCRIPT":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');
                var scriptPath = spaceIndex > 0 ? text[(spaceIndex + 1)..].Trim().Trim('"') : string.Empty;

                if (scriptPath.Length == 0)
                {
                    Log("SCRIPT: cần đường dẫn tệp .scr. Ví dụ: SCRIPT samples/demo.scr");
                    _statusHint = "SCRIPT: thiếu đường dẫn tệp";
                    UpdateStatus();
                    break;
                }

                if (!File.Exists(scriptPath))
                {
                    Log("SCRIPT: không tìm thấy '" + scriptPath + "'.");
                    _statusHint = "SCRIPT: không thấy tệp";
                    UpdateStatus();
                    break;
                }

                var entitiesBefore = _editor.Workspace.Document.Entities.ToList();
                var setBefore = new HashSet<ACadSharp.Entities.Entity>(entitiesBefore);

                OpenCad.Core.Scripting.ScriptResult scriptResult;

                try
                {
                    scriptResult = OpenCad.Core.Scripting.ScriptRunner.Run(
                        _editor.Workspace, File.ReadAllText(scriptPath));
                }
                catch (Exception ex)
                {
                    Log("SCRIPT: lỗi — " + ex.Message);
                    _statusHint = "SCRIPT: lỗi khi chạy";
                    UpdateStatus();
                    break;
                }

                _editor.Reload();

                // Undo: gỡ các đối tượng script thêm vào và trả lại các đối tượng bị nó xoá.
                // (Sửa tại chỗ bởi script thì không hoàn tác được — đã ghi rõ trong README.)
                var added = _editor.Workspace.Document.Entities
                    .Where(e => !setBefore.Contains(e))
                    .ToList();

                if (added.Count > 0 || scriptResult.Failed == 0)
                {
                    _editor.RunCustom("SCRIPT " + Path.GetFileName(scriptPath),
                        () => { _editor.Reload(); },
                        () =>
                        {
                            foreach (var entity in added) _editor.Workspace.Remove(entity);

                            foreach (var entity in entitiesBefore)
                                if (!_editor.Workspace.Document.Entities.Contains(entity))
                                    _editor.Workspace.Add(entity);

                            _editor.Reload();
                        });
                }

                foreach (var error in scriptResult.Errors.Take(8)) Log("   " + error);

                Log($"SCRIPT {Path.GetFileName(scriptPath)}: {scriptResult.Executed} lệnh, {scriptResult.Failed} lỗi");

                _statusHint = scriptResult.Ok
                    ? $"Script: {scriptResult.Executed} lệnh OK"
                    : $"Script: {scriptResult.Failed} lỗi / {scriptResult.Executed} lệnh";

                _canvas.ClearSelection();
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "PE":
            case "PEDIT":
            {
                if (_editor is null) break;

                var selection = _canvas.Selection.ToList();

                if (selection.Count == 0)
                {
                    Log("PEDIT: chọn polyline trước. Dùng: PE close | PE open | PE join");
                    _statusHint = "PEDIT: chưa chọn đối tượng";
                    UpdateStatus();
                    break;
                }

                var option = text.Contains(' ')
                    ? text[(text.IndexOf(' ') + 1)..].Trim().ToLowerInvariant()
                    : string.Empty;

                switch (option)
                {
                    case "c" or "close" or "đóng" or "dong":
                    {
                        var count = _editor.SetPolylineClosed(selection, true);

                        Log(count == 0
                            ? "PEDIT: không có polyline hở nào để đóng."
                            : $"PEDIT: đã đóng {count} polyline.");

                        _statusHint = count == 0 ? "PEDIT: không có gì để đóng" : $"Đã đóng {count} polyline";
                        break;
                    }

                    case "o" or "open" or "mở" or "mo":
                    {
                        var count = _editor.SetPolylineClosed(selection, false);

                        Log(count == 0
                            ? "PEDIT: không có polyline kín nào để mở."
                            : $"PEDIT: đã mở {count} polyline.");

                        _statusHint = count == 0 ? "PEDIT: không có gì để mở" : $"Đã mở {count} polyline";
                        break;
                    }

                    case "j" or "join" or "nối" or "noi":
                        _canvas.JoinSelection();
                        SyncSelection();
                        _statusHint = "PEDIT: đã nối các đoạn";
                        break;

                    default:
                        Log("PEDIT: chọn một tuỳ chọn — close (đóng), open (mở), join (nối).");
                        Log("   Ví dụ: PE close");
                        _statusHint = "PEDIT: close | open | join";
                        break;
                }

                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "LTSCALE":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');

                if (spaceIndex < 0 || spaceIndex + 1 >= text.Length)
                {
                    var first = _editor.Workspace.Document.Entities.FirstOrDefault();

                    Log("LTSCALE: tỷ lệ kiểu nét hiện tại = "
                        + (first?.LineTypeScale.ToString("0.###") ?? "1"));
                    Log("   Dùng: LTSCALE 2   (lớn hơn = nét dài hơn)");

                    _statusHint = "LTSCALE: nhập hệ số, ví dụ LTSCALE 2";
                    UpdateStatus();
                    break;
                }

                if (!double.TryParse(text[(spaceIndex + 1)..].Trim(), out var scale) || scale <= 0)
                {
                    Log("LTSCALE: hệ số không hợp lệ.");
                    _statusHint = "LTSCALE: hệ số phải > 0";
                    UpdateStatus();
                    break;
                }

                var changedCount = _editor.SetLineTypeScaleAll(scale);

                Log($"LTSCALE {scale:0.###}: đã áp cho {changedCount} đối tượng.");
                _statusHint = $"LTSCALE = {scale:0.###} ({changedCount} đối tượng)";
                _canvas.Invalidate();
                UpdateStatus();
                break;
            }

            case "LA":
            case "LAYER":
            {
                if (_editor is null) break;

                var spaceIndex = text.IndexOf(' ');

                if (spaceIndex > 0 && spaceIndex + 1 < text.Length)
                {
                    var name = text[(spaceIndex + 1)..].Trim();
                    var layer = _editor.Workspace.FindLayer(name);

                    if (layer is null)
                    {
                        Log("LAYER: không có layer '" + name + "'.");
                        _statusHint = "LAYER: không có layer này";
                        UpdateStatus();
                        break;
                    }

                    _canvas.CurrentLayer = layer;
                    _layers.RefreshLayers();

                    Log("LAYER: layer hiện hành = " + layer.Name);
                    _statusHint = "Layer hiện hành: " + layer.Name;
                    UpdateStatus();
                    break;
                }

                var all = _editor.Workspace.Document.Layers.Select(l => l.Name).OrderBy(n => n);

                Log("LAYER: " + string.Join(", ", all));
                _statusHint = "Layer: " + string.Join(", ", all.Take(8));
                UpdateStatus();
                break;
            }

            case "HATCHPATTERNS":
            {
                var names = OpenCad.Core.Styles.HatchPatternLibrary.Names;

                Log($"HATCHPATTERNS: {names.Count} mẫu tô");

                foreach (var name in names) Log("   " + name);

                _statusHint = "Mẫu tô: " + string.Join(", ", names);
                UpdateStatus();
                break;
            }

            case "LINETYPES":
            case "LINETYPELOAD":
            {
                if (_editor is null) break;

                var before = _editor.Workspace.Document.LineTypes.Count();

                var added = command == "LINETYPELOAD"
                    ? OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(_editor.Workspace.Document)
                    : 0;

                var names = _editor.Workspace.Document.LineTypes
                    .Select(l => l.Name)
                    .OrderBy(n => n)
                    .ToList();

                Log($"LINETYPES: {names.Count} kiểu nét");

                foreach (var name in names) Log("   " + name);

                _statusHint = added > 0
                    ? $"Đã nạp thêm {added} kiểu nét chuẩn (tổng {names.Count})"
                    : $"Có {names.Count} kiểu nét";

                if (added > 0)
                {
                    var current = _canvas.Selection.FirstOrDefault()?.LineType?.Name;

                    _props.RefreshLineTypes(
                        OpenCad.Core.Styles.LineTypeLibrary.Available(_editor.Workspace.Document),
                        current);

                    Log(_statusHint);
                }

                UpdateStatus();
                break;
            }
            case "ZOOMOBJ": case "ZO": _canvas.ZoomToSelection(); break;
            case "SELINVERT": _canvas.InvertSelection(); SyncSelection(); break;

            case "SELLAYER":
            case "SELTYPE":
            {
                var spaceIndex = text.IndexOf(' ');

                if (spaceIndex < 0 || spaceIndex + 1 >= text.Length)
                {
                    Log(command + ": cần tham số. Ví dụ SELLAYER WALLS hoặc SELTYPE Line");
                    _statusHint = command + ": thiếu tham số";
                    UpdateStatus();
                    break;
                }

                var argument = text[(spaceIndex + 1)..].Trim();

                var count = command.StartsWith("SELLAYER", StringComparison.OrdinalIgnoreCase)
                    ? _canvas.SelectByLayer(argument)
                    : _canvas.SelectByType(argument);

                SyncSelection();

                _statusHint = count == 0
                    ? $"{command} {argument}: không có đối tượng nào"
                    : $"{command} {argument}: đã chọn {count}";

                Log(_statusHint);
                UpdateStatus();
                break;
            }
            case "LAYOUTDEL": DeleteCurrentLayout(); break;
            case "LAYDEL": DeleteSelectedLayerWithContents(); break;
            case "DCO": case "DIMCONTINUE": SetMode(EditMode.DimContinue); break;
            case "DBA": case "DIMBASELINE": SetMode(EditMode.DimBaseline); break;
            case "TB": case "TABLE": _canvas.BeginTable(); break;
            case "LAYUNISO": ShowAllLayersAgain(); break;
            case "PASTECLIP": _canvas.PasteFromClipboard(); SyncSelection(); break;
            case "VS": case "VPSCALE": _canvas.BeginViewportScale(); break;
            case "AUDIT": RunAudit(); break;
            case "PURGE": RunPurge(); break;
            case "MA": case "MATCHPROP": SetMode(EditMode.MatchSource); break;

            case "E": case "ERASE": case "DEL": Erase(); break;
            case "ESC": case "ESCAPE": _canvas.Cancel(); break;
            case "SELECT": SetMode(EditMode.Select); break;

            case "U": case "UNDO":
            {
                // "U 5" = lùi 5 bước
                var spaceIndex = text.IndexOf(' ');

                if (spaceIndex > 0 && int.TryParse(text[(spaceIndex + 1)..].Trim(), out var steps) && steps > 1)
                {
                    var done = _editor.Undo(steps);
                    if (_canvas.IsPaperMode) _canvas.RefreshPaper();
                    _statusHint = $"UNDO: lùi {done} bước";
                    _canvas.Invalidate();
                    UpdateStatus();
                    break;
                }

                UndoAction();
                break;
            }
            case "REDO": RedoAction(); break;

            case "Z": case "ZOOM": _canvas.ZoomExtents(); break;
            case "ZW": case "ZOOMWINDOW": SetMode(EditMode.ZoomWindow); break;
            case "SPL": case "SPLINE": SetMode(EditMode.Spline); break;
            case "PO": case "POINT": SetMode(EditMode.PointDraw); break;
            case "ML": case "MLINE": SetMode(EditMode.MLine); break;
            case "ZP": case "ZOOMPREVIOUS": _canvas.ZoomPrevious(); UpdateStatus(); break;
            case "ZA": case "ZOOMALL": _canvas.ZoomExtents(); break;

            case "ORTHO": _canvas.ToggleOrtho(); break;
            case "POLAR": _canvas.TogglePolar(); break;
            case "OSNAP": case "OS": _canvas.ToggleSnap(); break;

            case "SAVE": case "QSAVE": SaveDocument(); break;
            case "SAVEAS": SaveDocumentAs(); break;
            case "PDF": case "EXPORTPDF": case "PLOT": ExportPdf(420, 297, null); break;
            case "OPEN": OpenDocument(); break;
            case "NEW": NewDocument(); break;

            case "SELECTALL": _canvas.SelectAll(); break;

            default:
                _command.SetPrompt($"Lệnh không hợp lệ: {text}");
                Log($"Lệnh không hợp lệ: {text}");
                handled = false;
                break;
        }

        if (handled) _lastCommand = text;

        FocusAfterCommand();
        UpdateStatus();
    }

    /// <summary>
    /// Trả focus về đúng chỗ: nếu lệnh đang chờ nhập số/chữ thì phải ở dòng lệnh,
    /// nếu không thì về canvas. Trước đây luôn trả về canvas nên ký tự nhập bị nuốt.
    /// </summary>
    private void FocusAfterCommand()
    {
        if (_canvas.AwaitingInput) _command.FocusInput();
        else _canvas.Focus();
    }

    /// <summary>Dọn layer/block/kiểu nét không dùng (PURGE). Chỉ xoá sau khi xác nhận.</summary>
    private void RunPurge()
    {
        if (_editor is null) return;

        var document = _editor.Workspace.Document;
        var plan = OpenCad.Core.Audit.DrawingPurge.Inspect(document, _canvas.CurrentLayer?.Name);

        if (plan.IsEmpty)
        {
            Log("PURGE: không có gì để dọn.");
            _statusHint = "PURGE: bản vẽ đã sạch.";
            UpdateStatus();
            return;
        }

        Log($"PURGE: có thể dọn {plan.Total} định nghĩa");
        if (plan.Layers.Count > 0) Log("   Layer   : " + string.Join(", ", plan.Layers));
        if (plan.Blocks.Count > 0) Log("   Block   : " + string.Join(", ", plan.Blocks));
        if (plan.LineTypes.Count > 0) Log("   Kiểu nét: " + string.Join(", ", plan.LineTypes));

        var answer = MessageBox.Show(this,
            $"Xoá {plan.Total} định nghĩa không dùng?"
            + Environment.NewLine + Environment.NewLine
            + $"Layer: {plan.Layers.Count}, Block: {plan.Blocks.Count}, Kiểu nét: {plan.LineTypes.Count}",
            "PURGE", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            Log("PURGE: đã huỷ.");
            return;
        }

        // Giữ tham chiếu để undo có thể thêm lại
        var layers = plan.Layers
            .Select(n => document.Layers.FirstOrDefault(l => l.Name == n))
            .Where(l => l is not null)
            .ToList()!;

        var blocks = plan.Blocks
            .Select(n => document.BlockRecords.FirstOrDefault(b => b.Name == n))
            .Where(b => b is not null)
            .ToList()!;

        var lineTypes = plan.LineTypes
            .Select(n => document.LineTypes.FirstOrDefault(l => l.Name == n))
            .Where(l => l is not null)
            .ToList()!;

        _editor.RunCustom("Purge",
            () => OpenCad.Core.Audit.DrawingPurge.Apply(document, plan),
            () =>
            {
                foreach (var layer in layers) { try { document.Layers.Add(layer!); } catch { } }
                foreach (var block in blocks) { try { document.BlockRecords.Add(block!); } catch { } }
                foreach (var lineType in lineTypes) { try { document.LineTypes.Add(lineType!); } catch { } }
            });

        Log($"PURGE: đã xoá {plan.Total} định nghĩa (undo được).");
        _statusHint = $"PURGE: đã dọn {plan.Total} định nghĩa";
        UpdateStatus();
    }

    /// <summary>Rà bản vẽ tìm lỗi dữ liệu (AUDIT).</summary>
    private void RunAudit()
    {
        if (_editor is null) return;

        var findings = OpenCad.Core.Audit.DrawingAudit.Run(_editor.Workspace.Document);

        if (findings.Count == 0)
        {
            Log("AUDIT: bản vẽ không có lỗi dữ liệu.");
            _statusHint = "AUDIT: không có lỗi.";
            UpdateStatus();
            return;
        }

        Log($"AUDIT: tìm thấy {findings.Count} vấn đề");

        foreach (var (code, count) in OpenCad.Core.Audit.DrawingAudit.Summary(findings))
            Log($"   {code}: {count}");

        Log("");

        foreach (var finding in findings.Take(30))
            Log("   " + finding);

        if (findings.Count > 30)
            Log($"   … và {findings.Count - 30} vấn đề nữa");

        _statusHint = $"AUDIT: {findings.Count} vấn đề — chi tiết ở F2";
        UpdateStatus();
    }

    /// <summary>
    /// In thông tin chẩn đoán: lỗi giao diện gần nhất, file log crash, số đối tượng.
    /// Dùng khi app có biểu hiện lạ — người dùng chỉ cần copy phần này gửi đi.
    /// </summary>
    private void ShowDiagnostics()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "opencad-crash.log");

        Log("CHẨN ĐOÁN OpenCAD");
        Log("   Phiên bản : " + typeof(MainForm).Assembly.GetName().Version);
        Log("   Tệp log   : " + logPath + (File.Exists(logPath) ? "  (có)" : "  (chưa có)"));

        if (_editor is not null)
        {
            Log("   Bản vẽ    : " + _editor.Workspace.DisplayName);
            Log("   Entity    : " + _editor.Workspace.Document.Entities.Count());
            Log("   Layer     : " + _editor.Workspace.Document.Layers.Count());
            Log("   Lựa chọn  : " + _canvas.Selection.Count);
        }

        Log("   Lỗi UI gần nhất: " + (CadCanvas.LastUiError ?? "không có"));

        if (File.Exists(logPath))
        {
            try
            {
                var tail = File.ReadAllLines(logPath).TakeLast(12);

                Log("");
                Log("   --- cuối file log ---");

                foreach (var line in tail) Log("   " + line);
            }
            catch (Exception ex)
            {
                Log("   (không đọc được log: " + ex.Message + ")");
            }
        }

        _statusHint = "DIAG: đã in chẩn đoán — xem cửa sổ lịch sử (F2)";
        UpdateStatus();
    }

    /// <summary>Cô lập layer của các đối tượng đang chọn (LAYISO).</summary>
    private void IsolateSelectedLayers()
    {
        if (_editor is null) return;

        var selection = _canvas.Selection.ToList();

        if (selection.Count == 0)
        {
            _statusHint = "LAYISO: chọn đối tượng trước đã.";
            UpdateStatus();
            return;
        }

        var turnedOff = _editor.IsolateLayers(selection);

        _canvas.Invalidate();
        _layers.RefreshLayers();

        _statusHint = turnedOff.Count == 0
            ? "LAYISO: không có layer nào cần ẩn."
            : $"LAYISO: đã ẩn {turnedOff.Count} layer";

        Log(_statusHint);
        UpdateStatus();
    }

    /// <summary>Bật lại mọi layer (LAYUNISO).</summary>
    private void ShowAllLayersAgain()
    {
        if (_editor is null) return;

        var shown = _editor.ShowAllLayers();

        _canvas.Invalidate();
        _layers.RefreshLayers();

        _statusHint = shown == 0 ? "LAYUNISO: mọi layer đã bật." : $"LAYUNISO: đã bật {shown} layer";

        Log(_statusHint);
        UpdateStatus();
    }

    /// <summary>In danh sách lệnh theo nhóm (gõ HELP hoặc ?).</summary>
    private void ListCommands()
    {
        var groups = OpenCad.Core.Commands.CommandRegistry.ByCategory();
        var total = OpenCad.Core.Commands.CommandRegistry.All.Length;

        Log($"DANH SÁCH LỆNH — {total} lệnh");

        foreach (var (category, commands) in groups)
        {
            Log("");
            Log($"[{category}]");

            foreach (var command in commands)
            {
                var aliases = command.Aliases.Length == 0
                    ? ""
                    : "  (" + string.Join(", ", command.Aliases) + ")";

                Log($"  {command.Name,-12}{aliases,-22} {command.Description}");
            }
        }

        _statusHint = $"{total} lệnh — xem chi tiết ở cửa sổ lịch sử (F2).";
        UpdateStatus();
    }

    /// <summary>Liệt kê các loại đối tượng không vẽ được — cảnh báo thay vì mất âm thầm.</summary>
    private void ListUnsupported()
    {
        if (_editor is null) return;

        var types = _editor.Scene.SkippedTypes;

        if (types.Count == 0)
        {
            Log("UNSUPPORTED: mọi đối tượng trong bản vẽ đều vẽ được.");
            _statusHint = "Không có đối tượng chưa hỗ trợ.";
            return;
        }

        Log($"UNSUPPORTED: {_editor.Scene.SkippedCount} đối tượng không vẽ được");
        foreach (var pair in types.OrderByDescending(p => p.Value))
            Log($"   {pair.Key}: {pair.Value}");

        _statusHint = "Chưa hỗ trợ: " + string.Join(", ", types.Select(p => p.Key).Take(4));
    }

    /// <summary>Liệt kê block có trong bản vẽ ra log và status bar.</summary>
    private void ListBlocks()
    {
        if (_editor is null) return;

        var blocks = OpenCad.Core.Editing.BlockLibrary.Named(_editor.Workspace.Document);

        if (blocks.Count == 0)
        {
            Log("BLOCKS: bản vẽ không có block nào.");
            _statusHint = "Không có block.";
            return;
        }

        Log($"BLOCKS: {blocks.Count} block");
        foreach (var block in blocks)
            Log($"   {block.Name}  ({OpenCad.Core.Editing.BlockLibrary.EntityCount(block)} entity)");

        _statusHint = $"Block: {string.Join(", ", blocks.Select(b => b.Name).Take(6))}" +
                      (blocks.Count > 6 ? " …" : string.Empty);
    }

    /// <summary>Liệt kê hoặc chọn style kích thước: DIMSTYLE (không tham số) = liệt kê.</summary>
    private void HandleDimStyle(string argument)
    {
        if (_editor is null) return;

        List<ACadSharp.Tables.DimensionStyle> styles;

        try { styles = _editor.Workspace.Document.DimensionStyles.ToList(); }
        catch { styles = new List<ACadSharp.Tables.DimensionStyle>(); }

        if (styles.Count == 0)
        {
            Log("DIMSTYLE: bản vẽ không có style kích thước nào.");
            _statusHint = "Không có dim style.";
            return;
        }

        // Dạng "chữ=150" / "mũi tên=80" / "thập phân=1" / "hệ số=0.001" / "hậu tố=m"
        if (argument.Contains('='))
        {
            ApplyDimensionStyleEdits(argument);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            Log($"DIMSTYLE: {styles.Count} style");
            foreach (var style in styles)
            {
                var current = ReferenceEquals(style, _canvas.CurrentDimensionStyle) ? " ◄" : string.Empty;
                Log($"   {style.Name}  (chữ {style.TextHeight:0.##}, mũi tên {style.ArrowSize:0.##}, hệ số đo {style.LinearScaleFactor:0.####}, tiền tố '{style.Prefix}', hậu tố '{style.Suffix}'){current}");
            }

            _statusHint = $"Dim style: {string.Join(", ", styles.Select(s => s.Name).Take(6))}";
            return;
        }

        var chosen = styles.FirstOrDefault(
            s => string.Equals(s.Name, argument, StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
        {
            Log($"DIMSTYLE: không có style '{argument}'.");
            _statusHint = $"Không có style '{argument}'.";
            return;
        }

        _canvas.CurrentDimensionStyle = chosen;
        Log($"DIMSTYLE: dùng '{chosen.Name}' (chữ {chosen.TextHeight:0.##}, mũi tên {chosen.ArrowSize:0.##})");
        _statusHint = $"Dim style: {chosen.Name}";
    }

    /// <summary>Áp thông số kiểu kích thước từ chuỗi "chữ=150, mũi tên=80, thập phân=1".</summary>
    private void ApplyDimensionStyleEdits(string argument)
    {
        if (_editor is null) return;

        var style = _canvas.CurrentDimensionStyle;

        if (style is null)
        {
            var styles = _editor.Workspace.Document.DimensionStyles.ToList();
            style = styles.FirstOrDefault();

            if (style is null)
            {
                Log("DIMSTYLE: bản vẽ không có kiểu kích thước nào.");
                return;
            }
        }

        double? text = null;
        double? arrow = null;
        int? decimals = null;
        double? scale = null;
        string? suffix = null;
        string? prefix = null;

        var changed = new List<string>();

        foreach (var token in argument.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = token.IndexOf('=');

            if (eq <= 0) continue;

            var key = token[..eq].Trim().ToLowerInvariant();
            var value = token[(eq + 1)..].Trim();

            switch (key)
            {
                case "chữ" or "chu" or "text" or "t":
                    if (double.TryParse(value, out var h) && h > 0) { text = h; changed.Add("chữ " + h); }
                    break;

                case "tiền tố" or "tien to" or "prefix" or "pre" or "p":
                    prefix = value;
                    changed.Add("tiền tố '" + value + "'");
                    break;

                case "mũi tên" or "mui ten" or "arrow" or "a":
                    if (double.TryParse(value, out var a) && a > 0) { arrow = a; changed.Add("mũi tên " + a); }
                    break;

                case "thập phân" or "thap phan" or "dec" or "d":
                    if (int.TryParse(value, out var dec) && dec is >= 0 and <= 8) { decimals = dec; changed.Add("thập phân " + dec); }
                    break;

                case "hệ số" or "he so" or "scale" or "s":
                    if (double.TryParse(value, out var sc) && sc > 0) { scale = sc; changed.Add("hệ số " + sc); }
                    break;

                case "hậu tố" or "hau to" or "suffix" or "p":
                    suffix = value;
                    changed.Add("hậu tố '" + value + "'");
                    break;
            }
        }

        if (changed.Count == 0)
        {
            Log("DIMSTYLE: không hiểu tham số. Ví dụ: DST chữ=150, mũi tên=80, thập phân=1");
            return;
        }

        _editor.SetDimensionStyleProperties(style, text, arrow, decimals, scale, suffix, "Dimension style", prefix);

        Log($"DIMSTYLE '{style.Name}': " + string.Join(", ", changed));
        _statusHint = $"Dim style {style.Name}: " + string.Join(", ", changed);
        _canvas.Invalidate();
    }

    /// <summary>Undo rồi vẽ lại tờ in nếu đang ở chế độ layout.</summary>
    private void UndoAction()
    {
        _canvas.Undo();

        if (_canvas.IsPaperMode) _canvas.RefreshPaper();

        UpdateStatus();
    }

    /// <summary>Redo rồi vẽ lại tờ in nếu đang ở chế độ layout.</summary>
    private void RedoAction()
    {
        _canvas.Redo();

        if (_canvas.IsPaperMode) _canvas.RefreshPaper();

        UpdateStatus();
    }

    /// <summary>Xoá lựa chọn hiện tại — ở model space hay trên tờ in.</summary>
    private void Erase()
    {
        if (!_canvas.IsPaperMode && _canvas.Selection.Count > 0)
        {
            var locked = _canvas.Selection.Count(CadEditor.IsEntityLocked);

            if (locked > 0)
            {
                Log($"ERASE: {locked} đối tượng nằm trên layer bị khoá — không xoá.");
                _statusHint = $"{locked} đối tượng trên layer khoá";
                UpdateStatus();
                return;
            }
        }

        if (_canvas.IsPaperMode)
        {
            if (!_canvas.DeletePaperSelection())
            {
                _statusHint = "Chưa chọn đối tượng nào trên tờ.";
                UpdateStatus();
            }

            return;
        }

        _canvas.DeleteSelection();
    }

    private void SetMode(EditMode mode)
    {
        _canvas.SetMode(mode);
        FocusAfterCommand();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- STATE

    private void SyncSelection()
    {
        var selection = _canvas.Selection.ToList();
        _editor.LastSelectionSnapshot = selection;
        _props.ShowSelection(selection);

        if (selection.Count > 0)
        {
            _props.RefreshLineTypes(
                OpenCad.Core.Styles.LineTypeLibrary.Available(_editor.Workspace.Document),
                selection[0].LineType?.Name);
        }

        UpdateStatus();
    }

    private void UpdateTitle()
    {
        var dirty = _editor.Workspace.IsModified ? " *" : string.Empty;
        Text = $"OpenCAD — {_editor.Workspace.DisplayName}{dirty}";
    }

    private void UpdateStatus()
    {
        if (_editor is null) return;

        var entities = 0;
        try { entities = _editor.Workspace.Document.Entities.Count(); } catch { }

        var parts = new List<string> { $"{entities} entity" };

        if (_editor.Scene.SkippedCount > 0)
            parts.Add($"{_editor.Scene.SkippedCount} chưa hỗ trợ");

        if (_canvas.Selection.Count > 0)
            parts.Add($"{_canvas.Selection.Count} chọn");

        if (_editor.Workspace.IsModified) parts.Add("chưa lưu");

        var skipped = _editor.Scene.SkippedCount;
        if (skipped > 0) parts.Add($"⚠ {skipped} đối tượng chưa hỗ trợ");

        _statusInfo.Text = string.Join(" · ", parts);
        if (!string.IsNullOrEmpty(_statusHint))
            _statusInfo.Text += " · " + _statusHint;

        StyleToggle(_toggleOsnap, _canvas.Snap.Enabled);
        StyleToggle(_toggleOrtho, _canvas.Snap.OrthoEnabled);
        StyleToggle(_togglePolar, _canvas.Snap.PolarEnabled);
        StyleToggle(_toggleGrid, _canvas.ShowGrid);
        StyleToggle(_toggleLwt, _canvas.ShowLineWeights);

        if (!_undoItem.Enabled && _editor.History.CanUndo) _undoItem.Enabled = true;
        _undoItem.Text = _editor.History.CanUndo ? $"&Undo {_editor.History.UndoName}" : "&Undo";
        _undoItem.Enabled = _editor.History.CanUndo;
        _redoItem.Text = _editor.History.CanRedo ? $"&Redo {_editor.History.RedoName}" : "&Redo";
        _redoItem.Enabled = _editor.History.CanRedo;
    }

    private static void StyleToggle(ToolStripStatusLabel label, bool active)
    {
        label.ForeColor = active ? Color.White : Color.FromArgb(160, 168, 180);
        label.BackColor = active ? AutoCadTheme.StatusActiveBlue : Color.Transparent;
        label.Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
    }

    // ------------------------------------------------------------- KEYBOARD

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);

        if (e.KeyChar < 32) return;
        if (_command.ContainsFocus || _canvas.AwaitingInput) return;

        _command.BeginTyping(e.KeyChar);
        e.Handled = true;
    }

    private bool ConfirmDiscard()
    {
        if (!_editor.Workspace.IsModified) return true;

        var result = MessageBox.Show(this,
            $"'{_editor.Workspace.DisplayName}' có thay đổi chưa lưu. Lưu lại trước khi tiếp tục?",
            "OpenCAD", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => SaveDocument(),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
