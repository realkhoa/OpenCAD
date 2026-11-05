namespace OpenCad.App.Panels;

/// <summary>
/// @brief Authentic AutoCAD command window control accepting typed commands and interactive parameters.
/// @details Features command history navigation (Up/Down arrow keys), real-time tab autocomplete hints,
/// dynamic AutoCAD prompt badge styling, and multi-language internationalization support.
/// </summary>
public sealed class CommandLine : UserControl
{
    private readonly TextBox _input = new();
    private readonly Label _promptBadge = new();
    private readonly Label _prompt = new();
    private readonly Label _hint = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    /// <summary>
    /// @brief Autocomplete suggestion provider returning the matching command or null.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, string?>? Suggest { get; set; }

    /// <summary>
    /// @brief Event triggered when a command line string is submitted via the Enter key.
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// @brief Event triggered when command input is aborted via the Escape key.
    /// </summary>
    public event Action? Cancelled;

    /// <summary>
    /// @brief Initializes a new instance of the CommandLine control with AutoCAD dark styling.
    /// </summary>
    public CommandLine()
    {
        Height = 32;
        BackColor = AutoCadTheme.CommandLineBackground;
        Padding = new Padding(4, 2, 4, 2);

        // AutoCAD "COMMAND:" badge on the left
        _promptBadge.Dock = DockStyle.Left;
        _promptBadge.Width = 72;
        _promptBadge.TextAlign = ContentAlignment.MiddleCenter;
        _promptBadge.ForeColor = AutoCadTheme.CyanAccent;
        _promptBadge.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _promptBadge.Text = I18n.T("CommandLine.Prompt");

        _prompt.Dock = DockStyle.Left;
        _prompt.Width = 320;
        _prompt.TextAlign = ContentAlignment.MiddleLeft;
        _prompt.Padding = new Padding(4, 0, 0, 0);
        _prompt.ForeColor = AutoCadTheme.TextPrimary;
        _prompt.Font = new Font("Segoe UI", 9f);
        _prompt.AutoEllipsis = true;
        _prompt.AutoSize = false;
        _prompt.Text = I18n.T("CommandLine.Ready");

        // Autocomplete hint on the right
        _hint.Dock = DockStyle.Right;
        _hint.Width = 200;
        _hint.TextAlign = ContentAlignment.MiddleLeft;
        _hint.ForeColor = Color.FromArgb(130, 145, 160);
        _hint.Font = new Font("Consolas", 9f, FontStyle.Italic);

        _input.Dock = DockStyle.Fill;
        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Color.FromArgb(24, 27, 32);
        _input.ForeColor = Color.White;
        _input.Font = new Font("Consolas", 10f);
        _input.Margin = new Padding(0, 4, 0, 0);
        _input.KeyDown += OnKeyDown;
        _input.TextChanged += (_, _) => UpdateHint();

        Controls.Add(_input);
        Controls.Add(_hint);
        Controls.Add(_prompt);
        Controls.Add(_promptBadge);

        I18n.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// @brief Gets the current input text (used by UI self-tests).
    /// </summary>
    internal string InputTextForTest => _input.Text;

    /// <summary>
    /// @brief Gets the current suggestion hint text (used by UI self-tests).
    /// </summary>
    internal string HintForTest => _hint.Text;

    /// <summary>
    /// @brief Sets the active prompt description displayed to the user.
    /// @param text The prompt text message.
    /// </summary>
    public void SetPrompt(string text)
    {
        if (text.Length == 0) return;
        _prompt.Text = text;
    }

    /// <summary>
    /// @brief Gives keyboard focus to the command line input box.
    /// </summary>
    public void FocusInput()
    {
        if (!_input.Focused) _input.Focus();
    }

    /// <summary>
    /// @brief Clears the current input text.
    /// </summary>
    public void Clear()
    {
        _input.Clear();
    }

    /// <summary>
    /// @brief Refreshes localized labels when application language changes.
    /// </summary>
    private void OnLanguageChanged()
    {
        _promptBadge.Text = I18n.T("CommandLine.Prompt");
        if (_prompt.Text == "Ready — enter a command or select objects" ||
            _prompt.Text == "Sẵn sàng — gõ lệnh hoặc chọn đối tượng")
        {
            _prompt.Text = I18n.T("CommandLine.Ready");
        }
        UpdateHint();
    }

    /// <summary>
    /// @brief Updates the predictive autocomplete hint based on the current user input.
    /// </summary>
    private void UpdateHint()
    {
        var suggestion = Suggest?.Invoke(_input.Text);
        _hint.Text = suggestion is null ? string.Empty : suggestion + "  (Tab)";
    }

    /// <summary>
    /// @brief Accepts the active suggestion hint when Tab is pressed.
    /// @return True if a suggestion was accepted; false otherwise.
    /// </summary>
    internal bool AcceptHint()
    {
        var suggestion = Suggest?.Invoke(_input.Text);
        if (suggestion is null) return false;

        _input.Text = suggestion;
        _input.SelectionStart = _input.TextLength;
        UpdateHint();
        return true;
    }

    /// <summary>
    /// @brief Forwards a character typed on the drawing canvas directly into the command line.
    /// @param c The character to type.
    /// </summary>
    public void BeginTyping(char c)
    {
        if (!_input.Focused) _input.Focus();
        _input.Text += c;
        _input.SelectionStart = _input.TextLength;
    }

    /// <summary>
    /// @brief Handles special command line keyboard shortcuts (Tab, Enter, Escape, Up, Down).
    /// @param sender Event source.
    /// @param e Key event arguments.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Tab:
                if (AcceptHint())
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                break;

            case Keys.Enter:
            {
                var text = _input.Text;
                _input.Clear();

                if (text.Trim().Length > 0)
                {
                    _history.Add(text);
                    if (_history.Count > 100) _history.RemoveAt(0);
                }

                _historyIndex = -1;
                Submitted?.Invoke(text);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            }

            case Keys.Escape:
                _input.Clear();
                _historyIndex = -1;
                Cancelled?.Invoke();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Up:
            case Keys.Down:
            {
                if (_history.Count == 0) break;

                if (e.KeyCode == Keys.Up)
                {
                    _historyIndex = _historyIndex < 0
                        ? _history.Count - 1
                        : Math.Max(0, _historyIndex - 1);
                }
                else
                {
                    if (_historyIndex < 0) break;
                    _historyIndex = Math.Min(_history.Count - 1, _historyIndex + 1);
                }

                _input.Text = _history[_historyIndex];
                _input.SelectionStart = _input.TextLength;
                UpdateHint();

                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            }
        }
    }
}
