using AcadColor = ACadSharp.Color;
using LineWeightType = ACadSharp.LineWeightType;
using ACadSharp.Entities;
using ACadSharp.Tables;
using OpenCad.Core.Editing;

namespace OpenCad.App.Panels;

/// <summary>Bảng thuộc tính: layer, màu, độ đậm nét và thông số hình học của đối tượng đang chọn.</summary>
public sealed class PropertiesPanel : UserControl
{
    private sealed record AciChoice(int Aci, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record WeightChoice(LineWeightType Weight, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly AciChoice[] ColorChoices =
    {
        new(0, "ByLayer"),
        new(1, "1 — Đỏ"),
        new(2, "2 — Vàng"),
        new(3, "3 — Xanh lá"),
        new(4, "4 — Cyan"),
        new(5, "5 — Xanh dương"),
        new(6, "6 — Magenta"),
        new(7, "7 — Trắng/Đen"),
        new(8, "8 — Xám đậm"),
        new(9, "9 — Xám nhạt"),
    };

    private static readonly WeightChoice[] WeightChoices =
    {
        new(LineWeightType.ByLayer, "ByLayer"),
        new(LineWeightType.W13, "0.13 mm"),
        new(LineWeightType.W18, "0.18 mm"),
        new(LineWeightType.W25, "0.25 mm"),
        new(LineWeightType.W35, "0.35 mm"),
        new(LineWeightType.W50, "0.50 mm"),
        new(LineWeightType.W70, "0.70 mm"),
    };

    private readonly Label _title = new();
    private readonly Label _summary = new();
    private readonly ComboBox _layerBox = new();
    private readonly Label _layerLabel = new();
    private readonly ComboBox _colorBox = new();
    private readonly Label _colorLabel = new();
    private readonly ComboBox _weightBox = new();
    private readonly ComboBox _lineTypeBox = new();
    private readonly Label _lineTypeLabel = new();
    private readonly Label _weightLabel = new();

    // Sửa hình học nhanh: bán kính (circle/arc) và chiều cao chữ
    private readonly Label _geometryLabel = new();
    private readonly TextBox _geometryBox = new();

    // Line cần hai ô: dài và góc
    private readonly Label _geometryLabel2 = new();
    private readonly TextBox _geometryBox2 = new();
    private Entity? _geometryTarget;
    private GeometryField _geometryField = GeometryField.None;

    private enum GeometryField { None, Radius, TextHeight, TextHeightAndRotation, LineLengthAndAngle }

    private CadEditor? _editor;
    private bool _loading;

    public event Action? LayerAssigned;
    public event Action? PropertiesAssigned;

    /// <summary>Người dùng chọn kiểu nét mới cho các đối tượng đang chọn.</summary>
    public event Action<ACadSharp.Tables.LineType>? LineTypeAssigned;

    public PropertiesPanel()
    {
        Width = 250;
        BackColor = Color.FromArgb(37, 40, 46);
        ForeColor = Color.Gainsboro;
        Padding = new Padding(8);

        _title.Text = "PROPERTIES";
        _title.Dock = DockStyle.Top;
        _title.Height = 24;
        _title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(0, 200, 255);

        ConfigureLabel(_layerLabel, "Layer");
        ConfigureCombo(_layerBox);
        _layerBox.SelectedIndexChanged += OnLayerChanged;

        ConfigureLabel(_colorLabel, "Màu");
        ConfigureCombo(_colorBox);
        foreach (var choice in ColorChoices) _colorBox.Items.Add(choice);
        _colorBox.SelectedIndexChanged += OnColorChanged;

        ConfigureLabel(_weightLabel, "Độ đậm nét");
        ConfigureCombo(_weightBox);
        foreach (var choice in WeightChoices) _weightBox.Items.Add(choice);
        _weightBox.SelectedIndexChanged += OnWeightChanged;

        ConfigureLabel(_lineTypeLabel, "Kiểu nét");
        ConfigureCombo(_lineTypeBox);
        _lineTypeBox.SelectedIndexChanged += OnLineTypeChanged;

        ConfigureLabel(_geometryLabel, "Bán kính");
        _geometryLabel.Visible = false;

        _geometryBox.Dock = DockStyle.Top;
        _geometryBox.Visible = false;
        _geometryBox.BackColor = Color.FromArgb(52, 56, 62);
        _geometryBox.ForeColor = Color.Gainsboro;
        _geometryBox.BorderStyle = BorderStyle.FixedSingle;
        _geometryBox.Font = new Font("Consolas", 9f);
        _geometryBox.KeyDown += OnGeometryKeyDown;
        _geometryBox.Leave += (_, _) => ApplyGeometryEdit();

        ConfigureLabel(_geometryLabel2, "Góc (°)");
        _geometryLabel2.Visible = false;

        _geometryBox2.Dock = DockStyle.Top;
        _geometryBox2.Visible = false;
        _geometryBox2.BackColor = Color.FromArgb(52, 56, 62);
        _geometryBox2.ForeColor = Color.Gainsboro;
        _geometryBox2.BorderStyle = BorderStyle.FixedSingle;
        _geometryBox2.Font = new Font("Consolas", 9f);
        _geometryBox2.KeyDown += OnGeometryKeyDown;
        _geometryBox2.Leave += (_, _) => ApplyGeometryEdit();

        _summary.Dock = DockStyle.Fill;
        _summary.Padding = new Padding(0, 10, 0, 0);
        _summary.ForeColor = Color.Gainsboro;
        _summary.Font = new Font("Consolas", 8.5f);

        // Thêm sau = ra ngoài (lên trên), nên thứ tự Add ngược với thứ tự hiển thị
        Controls.Add(_summary);
        Controls.Add(_geometryBox);
        Controls.Add(_geometryBox2);
        Controls.Add(_geometryLabel2);
        Controls.Add(_geometryLabel);
        Controls.Add(_weightBox);
        Controls.Add(_weightLabel);
        Controls.Add(_lineTypeBox);
        Controls.Add(_lineTypeLabel);
        Controls.Add(_colorBox);
        Controls.Add(_colorLabel);
        Controls.Add(_layerBox);
        Controls.Add(_layerLabel);
        Controls.Add(_title);

        I18n.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// @brief Refreshes localized labels and headings when the UI language changes.
    /// </summary>
    private void OnLanguageChanged()
    {
        _title.Text = I18n.T("Properties.Title");
        _layerLabel.Text = I18n.T("Properties.Layer");
        _colorLabel.Text = I18n.T("Properties.Color");
        _weightLabel.Text = I18n.T("Properties.Lineweight");
        _lineTypeLabel.Text = I18n.T("Properties.Linetype");
    }

    private static void ConfigureLabel(Label label, string text)
    {
        label.Text = text;
        label.Dock = DockStyle.Top;
        label.Height = 18;
        label.ForeColor = Color.Silver;
    }

    private static void ConfigureCombo(ComboBox box)
    {
        box.Dock = DockStyle.Top;
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = Color.FromArgb(30, 33, 38);
        box.ForeColor = Color.Gainsboro;
    }

    public void Attach(CadEditor? editor)
    {
        _editor = editor;
        RefreshLayers();
        ShowSelection(Array.Empty<Entity>());
    }

    public void RefreshLayers()
    {
        if (_editor is null) return;

        _loading = true;
        _layerBox.Items.Clear();

        try
        {
            foreach (var layer in _editor.Workspace.Document.Layers.OrderBy(l => l.Name))
                _layerBox.Items.Add(layer.Name);
        }
        catch { }

        _loading = false;
    }

    public void ShowSelection(IReadOnlyCollection<Entity> selection)
    {
        if (selection.Count == 0)
        {
            _summary.Text = "Chưa chọn đối tượng.";
            _layerBox.Enabled = false;
            _colorBox.Enabled = false;
            _weightBox.Enabled = false;
            BindGeometry(null);
            return;
        }

        _layerBox.Enabled = true;
        _colorBox.Enabled = true;
        _weightBox.Enabled = true;

        if (selection.Count == 1)
        {
            var e = selection.First();
            var layerName = SafeLayerName(e);

            _summary.Text = string.Join(Environment.NewLine, new[]
            {
                $"Loại      : {e.GetType().Name}",
                $"Handle    : {e.Handle:X}",
                $"Layer     : {layerName}",
                $"Màu       : {SafeColor(e)}",
                $"Đậm nét   : {e.LineWeight}",
                $"Invisible : {e.IsInvisible}",
                string.Empty,
                GeometrySummary(e),
            });

            _loading = true;
            _layerBox.SelectedItem = layerName;
            _colorBox.SelectedIndex = IndexOfColor(e);
            _weightBox.SelectedIndex = IndexOfWeight(e);
            _loading = false;

            BindGeometry(e);
        }
        else
        {
            _summary.Text = $"Đã chọn {selection.Count} đối tượng.";
            BindGeometry(null);

            _loading = true;
            _layerBox.SelectedIndex = -1;
            _colorBox.SelectedIndex = -1;
            _weightBox.SelectedIndex = -1;
            _loading = false;
        }
    }

    private static int IndexOfColor(Entity e)
    {
        try
        {
            var color = e.Color;
            if (color.IsByLayer || color.IsTrueColor) return 0;

            for (var i = 0; i < ColorChoices.Length; i++)
                if (ColorChoices[i].Aci == color.Index) return i;
        }
        catch { }

        return -1;
    }

    private static int IndexOfWeight(Entity e)
    {
        try
        {
            for (var i = 0; i < WeightChoices.Length; i++)
                if (WeightChoices[i].Weight == e.LineWeight) return i;
        }
        catch { }

        return -1;
    }

    private static string SafeLayerName(Entity e)
    {
        try { return e.Layer?.Name ?? "-"; }
        catch { return "-"; }
    }

    private static string SafeColor(Entity e)
    {
        try
        {
            var c = e.Color;
            if (c.IsByLayer) return "ByLayer";
            if (c.IsByBlock) return "ByBlock";
            return c.IsTrueColor ? $"RGB({c.R},{c.G},{c.B})" : $"ACI {c.Index}";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// Thông số hình học của một đối tượng — phần AutoCAD hay hiện trong Properties.
    /// Dài/góc/bán kính/diện tích/chu vi, tính theo toạ độ thật của bản vẽ.
    /// </summary>
    internal static string GeometrySummary(Entity e)
    {
        try
        {
            switch (e)
            {
                case Line l:
                {
                    var length = Distance(l.StartPoint, l.EndPoint);
                    var angle = Math.Atan2(l.EndPoint.Y - l.StartPoint.Y, l.EndPoint.X - l.StartPoint.X) * 180 / Math.PI;

                    return Form(
                        ("Điểm đầu", Fmt(l.StartPoint)),
                        ("Điểm cuối", Fmt(l.EndPoint)),
                        ("Dài", Num(length)),
                        ("Góc", Num(angle) + "°"),
                        ("ΔX", Num(l.EndPoint.X - l.StartPoint.X)),
                        ("ΔY", Num(l.EndPoint.Y - l.StartPoint.Y)));
                }

                case Arc a:
                {
                    var sweep = (a.EndAngle - a.StartAngle) * 180 / Math.PI;
                    if (sweep < 0) sweep += 360;

                    var arcLength = Math.Abs(a.EndAngle - a.StartAngle) * a.Radius;

                    return Form(
                        ("Tâm", Fmt(a.Center)),
                        ("Bán kính", Num(a.Radius)),
                        ("Góc đầu", Num(a.StartAngle * 180 / Math.PI) + "°"),
                        ("Góc cuối", Num(a.EndAngle * 180 / Math.PI) + "°"),
                        ("Tổng góc", Num(sweep) + "°"),
                        ("Dài cung", Num(arcLength)));
                }

                case Circle c:
                    return Form(
                        ("Tâm", Fmt(c.Center)),
                        ("Bán kính", Num(c.Radius)),
                        ("Đường kính", Num(c.Radius * 2)),
                        ("Chu vi", Num(2 * Math.PI * c.Radius)),
                        ("Diện tích", Num(Math.PI * c.Radius * c.Radius)));

                case Ellipse el:
                {
                    var major = Distance(el.Center, el.MajorAxisEndPoint);
                    var minor = major * el.RadiusRatio;

                    return Form(
                        ("Tâm", Fmt(el.Center)),
                        ("Bán trục lớn", Num(major)),
                        ("Bán trục nhỏ", Num(minor)),
                        ("Diện tích", Num(Math.PI * major * minor)));
                }

                case TextEntity t:
                    return Form(
                        ("Vị trí", Fmt(t.InsertPoint)),
                        ("Cao chữ", Num(t.Height)),
                        ("Xoay", Num(t.Rotation * 180 / Math.PI) + "°"),
                        ("Nội dung", t.Value ?? string.Empty));

                case MText m:
                    return Form(
                        ("Vị trí", Fmt(m.InsertPoint)),
                        ("Cao chữ", Num(m.Height)),
                        ("Số ký tự", (m.Value ?? string.Empty).Replace("\\P", " ").Length.ToString()),
                        ("Nội dung", (m.Value ?? string.Empty).Replace("\\P", " / ")));

                case LwPolyline p:
                {
                    var length = 0.0;

                    for (var i = 0; i + 1 < p.Vertices.Count; i++)
                        length += Distance(p.Vertices[i].Location, p.Vertices[i + 1].Location);

                    if (p.IsClosed && p.Vertices.Count > 2)
                        length += Distance(p.Vertices[^1].Location, p.Vertices[0].Location);

                    return Form(
                        ("Số đỉnh", p.Vertices.Count.ToString()),
                        ("Khép kín", p.IsClosed ? "Có" : "Không"),
                        ("Dài", Num(length)));
                }

                case Hatch h:
                    return Form(
                        ("Pattern", h.Pattern?.Name ?? "—"),
                        ("Tỷ lệ", Num(h.PatternScale)),
                        ("Góc", Num(h.PatternAngle * 180 / Math.PI) + "°"));

                case Dimension dim:
                    return Form(
                        ("Kiểu", dim.Style?.Name ?? "—"),
                        ("Đo", dim.Text ?? "(số đo thật)"),
                        ("Cao chữ", Num(dim.Style?.TextHeight ?? 0)));

                // TableEntity : Insert → phải đứng TRƯỚC case Insert
                case TableEntity tb:
                    return Form(
                        ("Số hàng", tb.RowCount.ToString()),
                        ("Số cột", tb.ColumnCount.ToString()),
                        ("Vị trí", Fmt(tb.InsertPoint)));

                case Insert i:
                    return Form(
                        ("Block", i.Block?.Name ?? "—"),
                        ("Vị trí", Fmt(i.InsertPoint)),
                        ("Scale", Num(i.XScale)),
                        ("Xoay", Num(i.Rotation * 180 / Math.PI) + "°"));

                case ACadSharp.Entities.Point pt:
                    return Form(("Vị trí", Fmt(pt.Location)));

                case Spline s:
                    return Form(
                        ("Số điểm điều khiển", s.ControlPoints.Count.ToString()),
                        ("Bậc", s.Degree.ToString()),
                        ("Khép kín", s.IsClosed ? "Có" : "Không"));

                default:
                    return string.Empty;
            }
        }
        catch { return string.Empty; }
    }

    /// <summary>Ghép các dòng "Nhãn : Giá trị" thẳng cột.</summary>
    private static string Form(params (string Label, string Value)[] rows)
        => string.Join(Environment.NewLine,
            rows.Select(r => r.Label.PadRight(9) + ": " + r.Value));

    private static string Num(double value) => value.ToString("0.###");

    private static double Distance(CSMath.XYZ a, CSMath.XYZ b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static double Distance(CSMath.XY a, CSMath.XY b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static string Fmt(CSMath.XYZ v) => $"{v.X:0.###}, {v.Y:0.###}";

    private static string Len(Line l)
    {
        var dx = l.EndPoint.X - l.StartPoint.X;
        var dy = l.EndPoint.Y - l.StartPoint.Y;
        return $"{Math.Sqrt(dx * dx + dy * dy):0.###}";
    }

    // ------------------------------------------------ SỬA HÌNH HỌC NHANH

    private void OnGeometryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyGeometryEdit();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            RefreshGeometryBox();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>Áp giá trị người dùng vừa gõ vào đối tượng đang chọn (có undo).</summary>
    internal bool ApplyGeometryEdit()
    {
        if (_editor is null || _geometryTarget is null) return false;
        if (_geometryField == GeometryField.None) return false;

        // Line dùng HAI ô (dài + góc) và cho phép để trống một ô → xử lý riêng TRƯỚC
        // khi kiểm tra "phải là số dương", vì ô dài trống là hợp lệ.
        if (_geometryField == GeometryField.LineLengthAndAngle)
        {
            return ApplyLineEdit();
        }

        // Chữ dùng HAI ô (cao chữ + góc xoay) như Line → xử lý riêng
        if (_geometryField == GeometryField.TextHeightAndRotation)
        {
            return ApplyTextEdit();
        }

        if (!double.TryParse(_geometryBox.Text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            return false;

        var changed = _geometryField switch
        {
            GeometryField.Radius => _editor.SetRadius(_geometryTarget, value),
            GeometryField.TextHeight => _editor.SetTextHeight(_geometryTarget, value),
            _ => false,
        };

        if (changed) PropertiesAssigned?.Invoke();

        // Hiện lại giá trị thật sau khi sửa
        RefreshGeometryBox();

        return changed;
    }

    /// <summary>Áp ô dài/góc cho Line: ô trống = giữ nguyên giá trị đó.</summary>
    private bool ApplyLineEdit()
    {
        double? length = null;
        double? angle = null;

        if (double.TryParse(_geometryBox.Text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lengthValue) &&
            lengthValue > 0)
            length = lengthValue;

        if (double.TryParse(_geometryBox2.Text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var angleValue))
            angle = angleValue;

        if (length is null && angle is null) return false;

        var changed = _editor!.SetLineGeometry(_geometryTarget!, length, angle);

        if (changed) PropertiesAssigned?.Invoke();

        RefreshGeometryBox();

        return changed;
    }

    /// <summary>
    /// Sửa chữ: ô 1 = cao chữ, ô 2 = góc xoay (độ). Cho phép để trống một ô.
    /// </summary>
    private bool ApplyTextEdit()
    {
        if (_geometryTarget is not TextEntity text) return false;

        var textChanged = false;

        if (double.TryParse(_geometryBox.Text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var heightValue) &&
            heightValue > 0 &&
            Math.Abs(heightValue - text.Height) > 1e-9)
        {
            textChanged |= _editor!.SetTextHeight(text, heightValue);
        }

        if (double.TryParse(_geometryBox2.Text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var angleValue))
        {
            var current = text.Rotation * 180 / Math.PI;

            if (Math.Abs(angleValue - current) > 1e-6)
                textChanged |= _editor!.SetTextRotation(new[] { text }, angleValue) > 0;
        }

        if (textChanged) PropertiesAssigned?.Invoke();

        RefreshGeometryBox();

        return textChanged;
    }

    /// <summary>Cập nhật ô sửa hình học theo đối tượng đang chọn (dùng cho kiểm thử).</summary>
    internal void RefreshGeometryBox()
    {
        switch (_geometryTarget)
        {
            case Arc arc when _geometryField == GeometryField.Radius:
                _geometryBox.Text = arc.Radius.ToString("0.###");
                break;

            case Circle circle when _geometryField == GeometryField.Radius:
                _geometryBox.Text = circle.Radius.ToString("0.###");
                break;

            case TextEntity text when _geometryField == GeometryField.TextHeight:
                _geometryBox.Text = text.Height.ToString("0.###");
                break;

            case TextEntity text when _geometryField == GeometryField.TextHeightAndRotation:
                _geometryBox.Text = text.Height.ToString("0.###");
                _geometryBox2.Text = (text.Rotation * 180 / Math.PI).ToString("0.##");
                break;

            case MText mtext when _geometryField == GeometryField.TextHeight:
                _geometryBox.Text = mtext.Height.ToString("0.###");
                break;

            case Line line when _geometryField == GeometryField.LineLengthAndAngle:
            {
                var dx = line.EndPoint.X - line.StartPoint.X;
                var dy = line.EndPoint.Y - line.StartPoint.Y;

                _geometryBox.Text = Math.Sqrt(dx * dx + dy * dy).ToString("0.###");
                _geometryBox2.Text = (Math.Atan2(dy, dx) * 180 / Math.PI).ToString("0.##");

                break;
            }

            default:
                _geometryBox.Text = string.Empty;
                _geometryBox2.Text = string.Empty;
                break;
        }
    }

    /// <summary>Chọn loại thông số sửa được cho một đối tượng.</summary>
    private void BindGeometry(Entity? entity)
    {
        _geometryTarget = entity;

        switch (entity)
        {
            case Arc:
            case Circle:
                _geometryField = GeometryField.Radius;
                _geometryLabel.Text = "Bán kính";
                break;

            case TextEntity:
                _geometryField = GeometryField.TextHeightAndRotation;
                _geometryLabel.Text = "Cao chữ";
                break;

            case MText:
                _geometryField = GeometryField.TextHeight;
                _geometryLabel.Text = "Cao chữ";
                break;

            case Line:
                _geometryField = GeometryField.LineLengthAndAngle;
                _geometryLabel.Text = "Dài";
                break;

            default:
                _geometryField = GeometryField.None;
                break;
        }

        var editable = _geometryField != GeometryField.None;
        var twoFields = _geometryField == GeometryField.LineLengthAndAngle;

        _geometryLabel.Visible = editable;
        _geometryBox.Visible = editable;

        _geometryLabel2.Visible = twoFields;
        _geometryBox2.Visible = twoFields;

        RefreshGeometryBox();
    }

    /// <summary>Đặt giá trị vào ô sửa và áp dụng — dùng cho kiểm thử.</summary>
    internal bool EditGeometryForTest(string value)
    {
        _geometryBox.Text = value;

        return ApplyGeometryEdit();
    }

    /// <summary>Đặt cả hai ô (dài, góc) rồi áp dụng — dùng cho kiểm thử.</summary>
    internal bool EditGeometryForTest(string first, string second)
    {
        _geometryBox.Text = first;
        _geometryBox2.Text = second;

        return ApplyGeometryEdit();
    }

    // ------------------------------------------------------------- HANDLERS

    private void OnLayerChanged(object? sender, EventArgs e)
    {
        if (_loading || _editor is null) return;
        if (_layerBox.SelectedItem is not string name) return;

        var layer = _editor.Workspace.FindLayer(name);
        if (layer is null) return;

        var targets = _editor.LastSelectionSnapshot;
        if (targets.Count == 0) return;

        _editor.SetLayer(targets, layer);
        LayerAssigned?.Invoke();
    }

    private void OnColorChanged(object? sender, EventArgs e)
    {
        if (_loading || _editor is null) return;
        if (_colorBox.SelectedItem is not AciChoice choice) return;

        var targets = _editor.LastSelectionSnapshot;
        if (targets.Count == 0) return;

        var color = choice.Aci == 0 ? AcadColor.ByLayer : new AcadColor((short)choice.Aci);
        _editor.SetColor(targets, color);
        PropertiesAssigned?.Invoke();
    }

    /// <summary>Nạp danh sách kiểu nét của bản vẽ, giữ lựa chọn hiện tại nếu có.</summary>
    public void RefreshLineTypes(IEnumerable<ACadSharp.Tables.LineType> types, string? current)
    {
        _lineTypeBox.SelectedIndexChanged -= OnLineTypeChanged;

        _lineTypeBox.Items.Clear();

        foreach (var type in types) _lineTypeBox.Items.Add(type.Name);

        if (current is not null)
        {
            var index = _lineTypeBox.Items.IndexOf(current);
            if (index >= 0) _lineTypeBox.SelectedIndex = index;
        }

        _lineTypeBox.SelectedIndexChanged += OnLineTypeChanged;
    }

    private void OnLineTypeChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (_lineTypeBox.SelectedItem is not string name) return;

        var type = _editor?.Workspace.Document.LineTypes
            .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

        if (type is not null) LineTypeAssigned?.Invoke(type);
    }

    private void OnWeightChanged(object? sender, EventArgs e)
    {
        if (_loading || _editor is null) return;
        if (_weightBox.SelectedItem is not WeightChoice choice) return;

        var targets = _editor.LastSelectionSnapshot;
        if (targets.Count == 0) return;

        _editor.SetLineWeight(targets, choice.Weight);
        PropertiesAssigned?.Invoke();
    }
}
