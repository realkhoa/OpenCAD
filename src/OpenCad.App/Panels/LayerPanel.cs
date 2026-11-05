using ACadSharp.Tables;
using OpenCad.Core.Editing;

namespace OpenCad.App.Panels;

/// <summary>Bảng layer: bật/tắt hiển thị, chọn layer hiện hành.</summary>
public sealed class LayerPanel : UserControl
{
    private readonly ListView _list = new();
    private readonly Label _title = new();
    private readonly FlowLayoutPanel _actions = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    private readonly Button _addButton = new();
    private readonly Button _colorButton = new();
    private readonly ContextMenuStrip _rowMenu = new();
    private readonly ToolStripMenuItem _menuLock = new();
    private readonly ToolStripMenuItem _menuFreeze = new();
    private readonly ToolStripMenuItem _menuCurrent = new();
    private readonly ToolStripMenuItem _menuRename = new();
    private readonly ToolStripMenuItem _menuDelete = new();
    private ACadSharp.Tables.Layer? _contextLayer;
    private CadEditor? _editor;
    private bool _loading;

    public event Action? LayersChanged;
    public event Action? LayerStateChanged;
    public event Action<Layer?>? CurrentLayerChanged;

    public Layer? CurrentLayer { get; private set; }

    /// <summary>Tên layer đang chọn trong danh sách (dùng cho LAYDEL).</summary>
    public string? SelectedLayerName =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Text : CurrentLayer?.Name;

    public LayerPanel()
    {
        Width = 220;
        BackColor = Color.FromArgb(37, 40, 46);
        ForeColor = Color.Gainsboro;
        Padding = new Padding(6);

        _title.Text = I18n.T("LayerPanel.Title");
        _title.Dock = DockStyle.Top;
        _title.Height = 24;
        _title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _title.ForeColor = AutoCadTheme.CyanAccent;

        _list.Dock = DockStyle.Fill;
        _list.View = System.Windows.Forms.View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.BackColor = Color.FromArgb(30, 33, 38);
        _list.ForeColor = Color.Gainsboro;
        _list.BorderStyle = BorderStyle.None;
        _list.Columns.Add("Tên", 95);
        _list.Columns.Add("Màu", 50);
        _list.Columns.Add("Trạng thái", 70);

        _list.ItemChecked += OnItemChecked;
        _list.SelectedIndexChanged += OnSelectedIndexChanged;

        _addButton.Text = "+ Layer";
        _addButton.FlatStyle = FlatStyle.Flat;
        _addButton.Height = 22;
        _addButton.Width = 76;
        _addButton.BackColor = Color.FromArgb(45, 48, 54);
        _addButton.ForeColor = Color.Gainsboro;
        _addButton.FlatAppearance.BorderColor = Color.FromArgb(70, 75, 85);
        _addButton.Click += OnAddLayer;

        _colorButton.Text = "Màu layer";
        _colorButton.FlatStyle = FlatStyle.Flat;
        _colorButton.Height = 22;
        _colorButton.Width = 84;
        _colorButton.BackColor = Color.FromArgb(45, 48, 54);
        _colorButton.ForeColor = Color.Gainsboro;
        _colorButton.FlatAppearance.BorderColor = Color.FromArgb(70, 75, 85);
        _colorButton.Click += OnChangeLayerColor;

        _actions.Dock = DockStyle.Top;
        _actions.Height = 28;
        _actions.Controls.Add(_addButton);
        _actions.Controls.Add(_colorButton);

        _menuLock.Text = "Khoá / Bỏ khoá";
        _menuLock.Click += (_, _) => ToggleFlag(ACadSharp.Tables.LayerFlags.Locked);

        _menuFreeze.Text = "Đóng băng / Bỏ đóng băng";
        _menuFreeze.Click += (_, _) => ToggleFlag(ACadSharp.Tables.LayerFlags.Frozen);

        _menuCurrent.Text = "Đặt làm layer hiện hành";
        _menuCurrent.Click += (_, _) =>
        {
            var layer = _contextLayer ?? CurrentLayer;
            if (layer is null) return;

            CurrentLayerChanged?.Invoke(layer);
        };

        _menuRename.Text = "Đổi tên layer…";
        _menuRename.Click += (_, _) => RenameContextLayer();

        _menuDelete.Text = "Xoá layer";
        _menuDelete.Click += (_, _) => DeleteContextLayer();

        _rowMenu.Items.Add(_menuRename);
        _rowMenu.Items.Add(_menuDelete);
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add(_menuLock);
        _rowMenu.Items.Add(_menuFreeze);
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add(_menuCurrent);

        _list.ContextMenuStrip = _rowMenu;

        // Chuột phải phải thao tác trên đúng dòng được bấm, không phải layer hiện hành
        _list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            var item = _list.GetItemAt(e.X, e.Y);
            _contextLayer = item?.Tag as ACadSharp.Tables.Layer;

            if (item is not null)
            {
                _list.FocusedItem = item;
                item.Selected = true;
            }
        };

        Controls.Add(_list);
        Controls.Add(_actions);
        Controls.Add(_title);

        I18n.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// @brief Updates all localized layer panel labels and menu items when the UI language changes.
    /// </summary>
    private void OnLanguageChanged()
    {
        _title.Text = I18n.T("LayerPanel.Title");
        if (_list.Columns.Count >= 3)
        {
            _list.Columns[0].Text = I18n.T("LayerPanel.ColName");
            _list.Columns[1].Text = I18n.T("LayerPanel.ColColor");
            _list.Columns[2].Text = I18n.T("LayerPanel.ColStatus");
        }
        _addButton.Text = I18n.T("LayerPanel.Add");
        _colorButton.Text = I18n.T("LayerPanel.Color");
        _menuLock.Text = I18n.T("LayerPanel.Lock");
        _menuFreeze.Text = I18n.T("LayerPanel.Freeze");
        _menuCurrent.Text = I18n.T("LayerPanel.SetCurrent");
        _menuRename.Text = I18n.T("LayerPanel.Rename");
        _menuDelete.Text = I18n.T("LayerPanel.Delete");
    }

    private void OnAddLayer(object? sender, EventArgs e)
    {
        if (_editor is null) return;

        var index = 1;
        string name;

        do { name = $"Layer{index++}"; }
        while (_editor.Workspace.FindLayer(name) is not null);

        var layer = _editor.CreateLayer(name, new ACadSharp.Color((short)7));
        if (layer is null) return;

        RefreshLayers();
        LayersChanged?.Invoke();
    }

    private void OnChangeLayerColor(object? sender, EventArgs e)
    {
        if (_editor is null || CurrentLayer is null) return;

        using var dialog = new ColorDialog { FullOpen = true, Color = SafeColor(CurrentLayer) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var c = dialog.Color;
        _editor.SetLayerColor(CurrentLayer, ACadSharp.Color.FromTrueColor(
            (uint)((c.R << 16) | (c.G << 8) | c.B)));

        RefreshLayers();
        LayersChanged?.Invoke();
    }

    public void Attach(CadEditor? editor)
    {
        _editor = editor;
        RefreshLayers();
    }

    public void RefreshLayers()    {
        if (_editor is null) return;

        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();

        IEnumerable<Layer> layers;
        try { layers = _editor.Workspace.Document.Layers.OrderBy(l => l.Name).ToList(); }
        catch { layers = Array.Empty<Layer>(); }

        foreach (var layer in layers)
        {
            var item = new ListViewItem(layer.Name) { Tag = layer, Checked = layer.IsOn };

            var color = SafeColor(layer);
            item.SubItems.Add(color.Name);
            item.SubItems.Add(DescribeState(layer));
            item.ForeColor = color;

            _list.Items.Add(item);
            if (CurrentLayer is not null && CurrentLayer.Name == layer.Name)
                item.Selected = true;
        }

        _list.EndUpdate();
        _loading = false;
    }

    /// <summary>Đổi tên layer đang được chuột phải.</summary>
    private void RenameContextLayer()
    {
        var layer = _contextLayer ?? CurrentLayer;

        if (_editor is null || layer is null) return;

        var newName = PromptForm.Ask(FindForm(), "Đổi tên layer", "Tên mới:", layer.Name);

        if (newName is null) return;

        if (!_editor.RenameLayer(layer, newName))
        {
            MessageBox.Show(FindForm(),
                "Không đổi được tên: tên trống hoặc đã có layer khác dùng tên đó.",
                "Đổi tên layer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RefreshLayers();
        LayersChanged?.Invoke();
    }

    /// <summary>Xoá layer đang được chuột phải (có kiểm tra an toàn).</summary>
    private void DeleteContextLayer()
    {
        var layer = _contextLayer ?? CurrentLayer;

        if (_editor is null || layer is null) return;

        var error = _editor.DeleteLayer(layer, CurrentLayer?.Name);

        if (error is not null)
        {
            MessageBox.Show(FindForm(), error, "Xoá layer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RefreshLayers();
        LayersChanged?.Invoke();
    }

    /// <summary>Bật/tắt một cờ của layer đang chọn (khoá / đóng băng).</summary>
    private void ToggleFlag(ACadSharp.Tables.LayerFlags flag)
    {
        var layer = _contextLayer ?? CurrentLayer;
        if (_editor is null || layer is null) return;

        _editor.RunCustom(
            "Layer state",
            () => layer.Flags ^= flag,
            () => layer.Flags ^= flag);

        RefreshLayers();
        LayerStateChanged?.Invoke();
    }

    /// <summary>Chọn layer hiện hành theo tên (dùng khi đổi từ toolbar).</summary>
    public void SelectLayer(string name)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is Layer layer && string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _loading = true;
                item.Selected = true;
                _loading = false;

                CurrentLayer = layer;
                return;
            }
        }
    }

    private static string DescribeState(ACadSharp.Tables.Layer layer)
    {
        var parts = new List<string>(2);

        if (!layer.IsOn) parts.Add("tắt");
        if ((layer.Flags & ACadSharp.Tables.LayerFlags.Frozen) != 0) parts.Add("băng");
        if ((layer.Flags & ACadSharp.Tables.LayerFlags.Locked) != 0) parts.Add("khoá");

        return parts.Count == 0 ? "—" : string.Join(" ", parts);
    }

    private static Color SafeColor(Layer layer)
    {
        try
        {
            var c = layer.Color;
            if (c.IsTrueColor) return Color.FromArgb(255, c.R, c.G, c.B);

            var rgb = OpenCad.Core.Colors.AciPalette.FromAci(c.Index);
            return Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        }
        catch
        {
            return Color.Gainsboro;
        }
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_loading || e.Item.Tag is not Layer layer) return;

        try { layer.IsOn = e.Item.Checked; }
        catch { return; }

        LayersChanged?.Invoke();
    }

    private void OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading || _list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not Layer layer) return;

        CurrentLayer = layer;
        CurrentLayerChanged?.Invoke(layer);
    }
}
