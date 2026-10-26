namespace OshiMakiKey.UI;

internal enum DialogKind { Info, Warning, Question }

/// <summary>Окно-сообщение в цветах темы: значок, заголовок, текст, флажок «больше не показывать» и свои кнопки.</summary>
internal sealed class ThemedDialog : Form
{
    readonly CheckBox? _check;
    int _result;

    ThemedDialog(IWin32Window? owner, string title, string heading, string text, IReadOnlyList<string> buttons, DialogKind kind, string? checkText, int cancelIndex)
    {
        _result = cancelIndex;
        Text = title;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;

        var (back, fore, glyph) = kind switch
        {
            DialogKind.Warning => (Theme.WarningSoft, Theme.Warning, "⚠"),
            DialogKind.Question => (Theme.AccentSoft, Theme.Accent, "?"),
            _ => (Theme.AccentSoft, Theme.Accent, "ℹ"),
        };

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(Theme.S(22), Theme.S(20), Theme.S(22), Theme.S(16)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(380)));

        var icon = new Label
        {
            Text = glyph,
            Font = new Font("Segoe UI Symbol", 16f, FontStyle.Bold),
            ForeColor = fore,
            BackColor = back,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(Theme.S(44), Theme.S(44)),
            Margin = new Padding(0, 0, Theme.S(16), 0),
        };
        using (var path = Theme.Rounded(new RectangleF(0, 0, icon.Width, icon.Height), icon.Width / 2f))
            icon.Region = new Region(path);
        root.Controls.Add(icon, 0, 0);
        root.SetRowSpan(icon, 3);

        var headingLabel = new Label
        {
            Text = heading,
            Font = Theme.Subtitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(Theme.S(380), 0),
            Margin = new Padding(0, Theme.S(2), 0, Theme.S(6)),
        };
        root.Controls.Add(headingLabel, 1, 0);

        var textLabel = new Label
        {
            Text = text,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(Theme.S(380), 0),
            Margin = new Padding(0, 0, 0, Theme.S(10)),
        };
        root.Controls.Add(textLabel, 1, 1);

        if (checkText != null)
        {
            _check = new CheckBox { Text = checkText, AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            root.Controls.Add(_check, 1, 2);
        }

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, Theme.S(14), 0, 0),
        };
        // Справа налево: последняя кнопка (обычно «Отмена») крайняя справа, первая — главная.
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            int index = i;
            var button = MakeButton(buttons[i], primary: i == 0);
            button.Click += (_, _) =>
            {
                _result = index;
                DialogResult = DialogResult.OK;
            };
            row.Controls.Add(button);
            if (i == 0) AcceptButton = button;
        }
        root.Controls.Add(row, 0, 3);
        root.SetColumnSpan(row, 2);
        Overlay.Prepare(this, Overlay.Card(root), owner);
        Theme.ApplyTo(this);
    }

    /// <summary>
    /// Показывает сообщение. Возвращает номер нажатой кнопки (крестик и Esc — <paramref name="cancelIndex"/>)
    /// и состояние флажка.
    /// </summary>
    public static (int Button, bool Checked) Show(IWin32Window? owner, string title, string heading, string text,
        IReadOnlyList<string> buttons, DialogKind kind = DialogKind.Info, string? checkText = null, int cancelIndex = -1)
    {
        using var dialog = new ThemedDialog(owner, title, heading, text, buttons, kind, checkText, cancelIndex);
        dialog.ShowDialog(owner);
        return (dialog._result, dialog._check?.Checked == true);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    internal static Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(Theme.S(100), Theme.S(34)),
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(Theme.S(8), 0, 0, 0),
            BackColor = primary ? Theme.Accent : Theme.Surface,
            ForeColor = primary ? Theme.OnAccent : Theme.Text,
            Font = primary ? Theme.Strong : Theme.Body,
        };
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentDark : Theme.Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Theme.AccentPressed : Theme.AccentSoft;
        return button;
    }
}
