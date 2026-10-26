using System.Diagnostics;
using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Страница «О программе» внутри главного окна: карточка по центру с шапкой цвета акцента, сведениями и ссылками.</summary>
internal sealed class AboutPage : UserControl
{
    readonly List<Image> _images = new();

    readonly Panel _card = new();

    public event EventHandler? Closed;

    public AboutPage(AppSettings settings)
    {
        Font = Theme.Body;
        BackColor = Theme.Window;
        Dock = DockStyle.Fill;
        _card.Size = new Size(Theme.S(560), Theme.S(600));
        _card.BackColor = Theme.Surface;

        var header = new Banner { Dock = DockStyle.Top, Height = Theme.S(150) };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(Theme.S(28), Theme.S(18), Theme.S(28), Theme.S(8)),
            BackColor = Theme.Surface,
            AutoScroll = true,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(string caption, Control value)
        {
            body.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(6), Theme.S(16), Theme.S(6)) });
            value.Anchor = AnchorStyles.Left;
            body.Controls.Add(value);
        }

        var description = new Label
        {
            Text = L.T("about.description", "Настройка китайских макро-клавиатур на чипах CH552 и CH57x: клавиши, ручки, мультимедиа, мышь и системные действия. Работает без установки — всё хранится рядом с программой."),
            AutoSize = true,
            ForeColor = Theme.Text,
            MaximumSize = new Size(Theme.S(460), 0),
            Margin = new Padding(0, 0, 0, Theme.S(14)),
        };
        body.Controls.Add(description);
        body.SetColumnSpan(description, 2);

        var driver = settings.EffectiveDriver;
        var driverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Surface };
        var icon = DriverIcon.Draw(driver, Theme.S(36), Theme.S(24));
        _images.Add(icon);
        driverRow.Controls.Add(new PictureBox { Image = icon, Size = icon.Size, Margin = new Padding(0, Theme.S(3), Theme.S(8), 0) });
        driverRow.Controls.Add(new Label { Text = driver.Title, AutoSize = true, Font = Theme.Strong, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(6), 0, 0) });
        Row(L.T("about.driver", "Драйвер"), driverRow);
        Row(L.T("about.theme", "Тема"), Value(ThemeStore.Current.Title));
        Row(L.T("about.language", "Язык"), Value(L.Available.FirstOrDefault(l => l.Code == L.Code)?.Name ?? L.T("menu.language.builtin", "Русский (встроенный)")));
        Row(L.T("about.data", "Данные"), Link(AppPaths.UserData, () => Open("explorer.exe", $"\"{AppPaths.UserData}\"")));

        var credits = new Label
        {
            Text = L.T("about.credits", "Протоколы плат восстановлены по открытым проектам:"),
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, Theme.S(16), 0, Theme.S(4)),
        };
        body.Controls.Add(credits);
        body.SetColumnSpan(credits, 2);
        foreach (var (name, url) in new[]
                 {
                     ("kriomant/ch57x-keyboard-tool", "https://github.com/kriomant/ch57x-keyboard-tool"),
                     ("rOzzy1987/MacroPad", "https://github.com/rOzzy1987/MacroPad"),
                 })
        {
            var link = Link(name, () => Open(url, null));
            body.Controls.Add(link);
            body.SetColumnSpan(link, 2);
        }

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(62),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(Theme.S(22), Theme.S(12), Theme.S(22), Theme.S(12)),
            BackColor = Theme.Window,
        };
        var close = ThemedDialog.MakeButton(L.T("button.close", "Закрыть"), primary: true);
        close.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        bottom.Controls.Add(close);
        var site = ThemedDialog.MakeButton(L.T("about.github", "Страница на GitHub"), primary: false);
        site.Click += (_, _) => Open(Branding.Website, null);
        bottom.Controls.Add(site);

        _card.Controls.Add(body);
        _card.Controls.Add(bottom);
        _card.Controls.Add(header);
        Controls.Add(_card);
        ForeColor = Theme.Text;
        Theme.Style(this);
    }

    // Карточка всегда по центру страницы.
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _card.Location = new Point(Math.Max(0, (ClientSize.Width - _card.Width) / 2), Math.Max(0, (ClientSize.Height - _card.Height) / 2));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Escape or Keys.Enter)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    static Label Value(string text) => new() { Text = text, AutoSize = true, Font = Theme.Strong, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(6), 0, Theme.S(6)) };

    static LinkLabel Link(string text, Action click)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentDark,
            VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            MaximumSize = new Size(Theme.S(360), 0),
            Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)),
        };
        link.LinkClicked += (_, _) => click();
        return link;
    }

    void Open(string target, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target, arguments ?? "") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ThemedDialog.Show(this, Branding.AppName, ex.Message, target, new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var image in _images) image.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Шапка: градиент акцента, крупная иконка, название и версия.</summary>
    sealed class Banner : Control
    {
        readonly Image _icon;

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using var stream = new MemoryStream(Theme.BuildIconBytes());
            using var icon = new Icon(stream, 64, 64);
            _icon = icon.ToBitmap();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.Scale;
            using (var brush = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.AccentPressed, 25f))
                g.FillRectangle(brush, ClientRectangle);
            // Декоративные круги в духе ручки устройства.
            using (var pen = new Pen(Color.FromArgb(40, Theme.OnAccent), 10 * s))
            {
                g.DrawEllipse(pen, Width - 150 * s, -60 * s, 220 * s, 220 * s);
                g.DrawEllipse(pen, Width - 90 * s, 40 * s, 120 * s, 120 * s);
            }
            float size = 72 * s;
            var iconRect = new RectangleF(28 * s, (Height - size) / 2, size, size);
            using (var shadow = Theme.Rounded(new RectangleF(iconRect.X, iconRect.Y + 4 * s, size, size), 16 * s))
            using (var brush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillPath(brush, shadow);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, iconRect);

            float x = iconRect.Right + 20 * s;
            TextRenderer.DrawText(g, Branding.AppName, Theme.Title, new Point((int)x, (int)(Height / 2 - 30 * s)), Theme.OnAccent, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, L.F("about.version", "Версия {0}", Branding.Version), Theme.Body,
                new Point((int)x, (int)(Height / 2 + 6 * s)), Color.FromArgb(220, Theme.OnAccent), TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _icon.Dispose();
            base.Dispose(disposing);
        }
    }
}
