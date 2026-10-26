using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Маленькое окно с одним полем ввода (название пресета и т.п.).</summary>
internal sealed class InputDialog : Form
{
    readonly TextBox _box = new();
    readonly Label _error = new();
    readonly Func<string, string?>? _validate;

    InputDialog(IWin32Window? owner, string title, string prompt, string initial, Func<string, string?>? validate)
    {
        _validate = validate;

        Text = title;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(Theme.S(20), Theme.S(16), Theme.S(20), Theme.S(14)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(380)));

        var label = new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(8)) };

        _box.Text = initial;
        _box.Dock = DockStyle.Fill;
        _box.Margin = new Padding(0);
        _box.TextChanged += (_, _) => _error.Visible = false;

        _error.AutoSize = true;
        _error.ForeColor = Theme.Danger;
        _error.Visible = false;
        _error.MaximumSize = new Size(Theme.S(380), 0);
        _error.Margin = new Padding(0, Theme.S(6), 0, 0);

        var ok = MakeButton(L.T("button.ok", "ОК"), primary: true);
        var cancel = MakeButton(L.T("button.cancel", "Отмена"), primary: false);
        ok.Click += (_, _) => Confirm();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, Theme.S(14), 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        // Заголовок окна в режиме «внутри окна» не виден — показываем его в карточке.
        root.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.Subtitle, Margin = new Padding(0, 0, 0, Theme.S(10)) }, 0, 0);
        root.Controls.Add(label, 0, 1);
        root.Controls.Add(_box, 0, 2);
        root.Controls.Add(_error, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Overlay.Prepare(this, Overlay.Card(root), owner);

        AcceptButton = ok;
        CancelButton = cancel;
        Theme.ApplyTo(this);
    }

    /// <summary>
    /// Спрашивает строку. <paramref name="validate"/> возвращает текст ошибки (окно не закроется) или null.
    /// Null в ответе — пользователь отменил ввод.
    /// </summary>
    public static string? Ask(IWin32Window? owner, string title, string prompt, string initial = "", Func<string, string?>? validate = null)
    {
        using var dialog = new InputDialog(owner, title, prompt, initial, validate);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._box.Text.Trim() : null;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.SelectAll();
        _box.Focus();
    }

    void Confirm()
    {
        if (_validate?.Invoke(_box.Text.Trim()) is string error)
        {
            _error.Text = error;
            _error.Visible = true;
            _box.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
    }

    static Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(Theme.S(96), Theme.S(32)),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(Theme.S(8), 0, 0, 0),
            BackColor = primary ? Theme.Accent : Theme.Surface,
            ForeColor = primary ? Theme.OnAccent : Theme.Text,
        };
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentDark : Theme.Hover;
        return button;
    }
}
