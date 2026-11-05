namespace OpenCad.App.Panels;

/// <summary>Hộp thoại nhập một dòng văn bản — dùng cho đổi tên layer.</summary>
internal sealed class PromptForm : Form
{
    private readonly TextBox _input = new();

    private PromptForm(string title, string label, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(340, 110);
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = Color.Gainsboro;

        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.Silver,
            Padding = new Padding(4, 4, 0, 0),
        };

        _input.Dock = DockStyle.Top;
        _input.Text = initial;
        _input.BackColor = Color.FromArgb(30, 30, 30);
        _input.ForeColor = Color.White;
        _input.BorderStyle = BorderStyle.FixedSingle;

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Right,
            Width = 80,
        };

        var cancel = new Button
        {
            Text = "Huỷ",
            DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Right,
            Width = 80,
        };

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        // Thêm ngược để ra đúng thứ tự trên xuống
        Controls.Add(_input);
        Controls.Add(caption);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    /// <summary>Hiện hộp thoại; trả về null nếu người dùng huỷ.</summary>
    public static string? Ask(IWin32Window? owner, string title, string label, string initial)
    {
        using var form = new PromptForm(title, label, initial);

        return form.ShowDialog(owner) == DialogResult.OK ? form._input.Text : null;
    }
}
