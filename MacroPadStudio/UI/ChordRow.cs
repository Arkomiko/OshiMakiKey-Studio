using System.ComponentModel;
using OshiMakiKey.Agent;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Поле, которое «записывает» нажатую на клавиатуре комбинацию.</summary>
internal sealed class KeyCaptureBox : TextBox
{
    string _display = "";
    KeyChord _pending = new();
    bool _captured;

    public event Action<KeyChord>? Captured;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string DisplayText
    {
        get => _display;
        set
        {
            _display = value;
            Text = value;
        }
    }

    public KeyCaptureBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = Theme.Input;
        ForeColor = Theme.InputText;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        Font = Theme.Strong;
        PlaceholderText = L.T("capture.placeholder", "щёлкните и нажмите клавиши");
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        BackColor = Theme.AccentSoft;
        ForeColor = Theme.AccentText;
        ResetCapture();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        BackColor = Theme.Input;
        ForeColor = Theme.InputText;
        ResetCapture();
    }

    void ResetCapture()
    {
        _pending = new KeyChord();
        _captured = false;
        Text = _display;
    }

    // Все клавиши (Tab, стрелки, Enter, Alt…) должны доходить до OnKeyDown, а не обрабатываться формой.
    protected override bool IsInputKey(Keys keyData) => true;
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (IsModifier(e.KeyCode))
        {
            if (_captured)
            {
                _pending = new KeyChord();
                _captured = false;
            }
            _pending.Ctrl |= e.Control;
            _pending.Shift |= e.Shift;
            _pending.Alt |= e.Alt;
            _pending.Win |= e.KeyCode is Keys.LWin or Keys.RWin || WinDown();
            Text = BindingText.Chord(_pending) + " + …";
            return;
        }

        byte code = KeyCatalog.FromVirtualKey(e.KeyCode);
        if (code == 0) return;
        _captured = true;
        Captured?.Invoke(new KeyChord { Ctrl = e.Control, Shift = e.Shift, Alt = e.Alt, Win = WinDown(), Key = code });
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        // Print Screen приходит только как отпускание клавиши.
        if (e.KeyCode == Keys.PrintScreen)
        {
            _captured = true;
            Captured?.Invoke(new KeyChord { Ctrl = e.Control, Shift = e.Shift, Alt = e.Alt, Win = WinDown(), Key = KeyCatalog.PrintScreen });
            return;
        }

        if (IsModifier(e.KeyCode) && !AnyModifierDown())
        {
            // Отпустили только модификаторы (например, Ctrl + Shift) — записываем их без основной клавиши.
            if (!_captured && !_pending.IsEmpty) Captured?.Invoke(_pending);
            _pending = new KeyChord();
            _captured = false;
            Text = _display;
        }
    }

    static bool IsModifier(Keys key) => key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu
        or Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey
        or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    static bool WinDown() => Win32.IsDown(Win32.VK_LWIN) || Win32.IsDown(Win32.VK_RWIN);

    static bool AnyModifierDown() => Win32.IsDown(Win32.VK_SHIFT) || Win32.IsDown(Win32.VK_CONTROL) ||
                                     Win32.IsDown(Win32.VK_MENU) || WinDown();
}

/// <summary>Строка редактора одного нажатия: поле записи, флажки модификаторов, выбор клавиши из списка.</summary>
internal sealed class ChordRow : FlowLayoutPanel
{
    readonly Choice<byte>[] _keyChoices = new[] { new Choice<byte>(0, L.T("chord.noKey", "(без клавиши)")) }
        .Concat(KeyCatalog.All.Select(k => new Choice<byte>(k.Code, k.ListName)))
        .ToArray();

    readonly KeyChord _chord;
    readonly KeyCaptureBox _capture = new();
    readonly CheckBox _ctrl = Modifier("Ctrl"), _shift = Modifier("Shift"), _alt = Modifier("Alt"), _win = Modifier("Win");
    readonly ThemedComboBox _key = new();
    readonly Button _remove = new();
    readonly ToolTip _tips = new();
    bool _loading;

    public event EventHandler? Changed;
    public event EventHandler? RemoveRequested;

    public ChordRow(KeyChord chord)
    {
        _chord = chord;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(0, 0, 0, Theme.S(8));
        Padding = new Padding(0);
        BackColor = Theme.Surface;

        _capture.Width = Theme.S(170);
        _capture.Margin = new Padding(0, Theme.S(1), Theme.S(12), 0);
        _capture.Captured += OnCaptured;

        _key.DropDownStyle = ComboBoxStyle.DropDownList;
        _key.Width = Theme.S(132);
        _key.MaxDropDownItems = 18;
        _key.Margin = new Padding(Theme.S(4), Theme.S(1), Theme.S(4), 0);
        _key.Items.AddRange(_keyChoices);

        _remove.Text = "✕";
        _remove.Size = new Size(Theme.S(30), Theme.S(27));
        _remove.FlatStyle = FlatStyle.Flat;
        _remove.FlatAppearance.BorderSize = 0;
        _remove.FlatAppearance.MouseOverBackColor = Theme.Hover;
        _remove.ForeColor = Theme.Muted;
        _remove.Cursor = Cursors.Hand;
        _remove.Margin = new Padding(0);

        _tips.SetToolTip(_capture, L.T("chord.captureTip", "Щёлкните сюда и нажмите нужную комбинацию на клавиатуре"));
        _tips.SetToolTip(_key, L.T("chord.keyTip", "Клавишу можно выбрать и вручную"));
        _tips.SetToolTip(_remove, L.T("chord.removeTip", "Удалить это нажатие"));

        Controls.AddRange(new Control[] { _capture, _ctrl, _shift, _alt, _win, _key, _remove });
        Theme.Style(this);
        LoadFromChord();

        foreach (var box in new[] { _ctrl, _shift, _alt, _win }) box.CheckedChanged += (_, _) => FromControls();
        _key.SelectedIndexChanged += (_, _) => FromControls();
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    public void FocusCapture() => _capture.Focus();

    static CheckBox Modifier(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, Theme.S(4), Theme.S(4), 0),
    };

    void LoadFromChord()
    {
        _loading = true;
        _ctrl.Checked = _chord.Ctrl;
        _shift.Checked = _chord.Shift;
        _alt.Checked = _chord.Alt;
        _win.Checked = _chord.Win;
        int index = Array.FindIndex(_keyChoices, c => c.Value == _chord.Key);
        _key.SelectedIndex = index < 0 ? 0 : index;
        _capture.DisplayText = _chord.IsEmpty ? "" : BindingText.Chord(_chord);
        _loading = false;
    }

    void OnCaptured(KeyChord captured)
    {
        _chord.Ctrl = captured.Ctrl;
        _chord.Shift = captured.Shift;
        _chord.Alt = captured.Alt;
        _chord.Win = captured.Win;
        _chord.Key = captured.Key;
        LoadFromChord();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void FromControls()
    {
        if (_loading) return;
        _chord.Ctrl = _ctrl.Checked;
        _chord.Shift = _shift.Checked;
        _chord.Alt = _alt.Checked;
        _chord.Win = _win.Checked;
        _chord.Key = _key.SelectedItem is Choice<byte> choice ? choice.Value : (byte)0;
        _capture.DisplayText = _chord.IsEmpty ? "" : BindingText.Chord(_chord);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
