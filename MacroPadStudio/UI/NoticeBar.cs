using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Полоса внутренних уведомлений под шапкой окна: нет файлов тем, языков, драйверов и т.п.</summary>
internal sealed class NoticeBar : Panel
{
    readonly TableLayoutPanel _rows = new();
    readonly List<Label> _texts = new();
    readonly ToolTip _tips = new();

    public NoticeBar()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Border;
        Padding = new Padding(0, 0, 0, 1);

        _rows.Dock = DockStyle.Top;
        _rows.AutoSize = true;
        _rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rows.ColumnCount = 1;
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rows.Margin = new Padding(0);
        Controls.Add(_rows);

        Notices.Changed += Rebuild;
        Rebuild();
    }

    void Rebuild()
    {
        SuspendLayout();
        foreach (var control in _rows.Controls.Cast<Control>().ToList()) control.Dispose();
        _rows.Controls.Clear();
        _rows.RowStyles.Clear();
        _texts.Clear();
        _rows.RowCount = Notices.All.Count;
        for (int i = 0; i < Notices.All.Count; i++)
        {
            _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rows.Controls.Add(Row(Notices.All[i]), 0, i);
        }
        Visible = Notices.All.Count > 0;
        UpdateTextWidth();
        ResumeLayout(true);
    }

    Control Row(Notice notice)
    {
        var back = notice.Level == NoticeLevel.Info ? Theme.AccentSoft : Theme.WarningSoft;
        var fore = notice.Level == NoticeLevel.Info ? Theme.AccentText : Theme.WarningText;

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = back,
            Margin = new Padding(0),
            Padding = new Padding(Theme.S(18), Theme.S(6), Theme.S(10), Theme.S(6)),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icon = new Label
        {
            Text = notice.Level == NoticeLevel.Info ? "ℹ" : "⚠",
            AutoSize = true,
            Font = Theme.SymbolSmall,
            ForeColor = notice.Level == NoticeLevel.Error ? Theme.Danger : fore,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, Theme.S(8), 0),
        };
        var text = new Label { Text = notice.Text, AutoSize = true, ForeColor = fore, Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(2), Theme.S(12), Theme.S(2)) };
        _texts.Add(text);
        row.Controls.Add(icon, 0, 0);
        row.Controls.Add(text, 1, 0);

        if (notice.SuggestUpdate)
        {
            // Проверка обновлений и целостности файлов ещё не готова — кнопка видна, но неактивна.
            var update = new Label
            {
                Text = L.T("menu.updates", "Проверить обновления и файлы"),
                AutoSize = true,
                Font = Theme.Strong,
                ForeColor = Theme.Blend(back, fore, 0.5f),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 0, Theme.S(10), 0),
            };
            _tips.SetToolTip(update, L.T("menu.updates.soon", "Скоро"));
            row.Controls.Add(update, 2, 0);
        }

        var close = new Button
        {
            Text = "✕",
            Size = new Size(Theme.S(28), Theme.S(26)),
            FlatStyle = FlatStyle.Flat,
            ForeColor = fore,
            BackColor = back,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Theme.Blend(back, fore, 0.12f);
        close.Click += (_, _) => Notices.Remove(notice);
        _tips.SetToolTip(close, L.T("notice.dismiss", "Скрыть уведомление"));
        row.Controls.Add(close, 3, 0);
        return row;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateTextWidth();
    }

    /// <summary>Длинный текст переносится по ширине окна, оставляя место для кнопок справа.</summary>
    void UpdateTextWidth()
    {
        int width = Math.Max(Theme.S(200), ClientSize.Width - Theme.S(280));
        foreach (var text in _texts) text.MaximumSize = new Size(width, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Notices.Changed -= Rebuild;
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
