using System.Diagnostics;
using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Страница «О программе» внутри главного окна: поверх полупрозрачного снимка окна — закруглённая карточка
/// с шапкой цвета акцента, а сведения и ссылки сгруппированы в закруглённые рамки.
/// </summary>
internal sealed class AboutPage : UserControl
{
    // Фон страницы прозрачен на 70%: окно под ней видно, но приглушено цветом темы.
    const int BackdropOpacity = 255 * 30 / 100;

    readonly List<Image> _images = new();
    readonly Bitmap? _backdrop;
    readonly RoundPanel _card;
    readonly FlowLayoutPanel _body;
    readonly FlowLayoutPanel _buttons;

    public event EventHandler? Closed;

    public AboutPage(AppSettings settings, Bitmap? backdrop)
    {
        _backdrop = backdrop;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Dock = DockStyle.Fill;

        int radius = Theme.S(16);
        // Ширина текста — с запасом под вертикальную полосу прокрутки, иначе при её появлении правый край букв обрезается.
        int inner = Theme.S(560) - Theme.S(26) - Theme.S(18) - SystemInformation.VerticalScrollBarWidth - 2;
        _card = new RoundPanel { Fill = Theme.Surface, Line = Theme.Border, Radius = radius, Width = Theme.S(560), Padding = new Padding(1, 1, 1, radius) };
        var header = new Banner(radius) { Dock = DockStyle.Top, Height = Theme.S(150) };

        _body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(Theme.S(26), Theme.S(18), Theme.S(18), 0),
            BackColor = Theme.Surface,
        };

        _body.Controls.Add(new Label
        {
            Text = L.T("about.description", "Настройка китайских макро-клавиатур на чипах CH552 и CH57x: клавиши, ручки, мультимедиа, мышь и системные действия. Работает без установки — всё хранится рядом с программой."),
            AutoSize = true,
            ForeColor = Theme.Text,
            MaximumSize = new Size(inner, 0),
            Margin = new Padding(0, 0, 0, Theme.S(14)),
        });

        // Рамка со сведениями: драйвер, тема, язык (папка данных показана в настройках).
        var details = Frame(inner);
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = details.Fill, Dock = DockStyle.Top };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string caption, Control value)
        {
            table.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(5), Theme.S(16), Theme.S(5)) });
            value.Anchor = AnchorStyles.Left;
            table.Controls.Add(value);
        }

        var driver = settings.EffectiveDriver;
        var driverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = details.Fill };
        var icon = DriverIcon.Draw(driver, Theme.S(36), Theme.S(24));
        _images.Add(icon);
        driverRow.Controls.Add(new PictureBox { Image = icon, Size = icon.Size, Margin = new Padding(0, Theme.S(3), Theme.S(8), 0) });
        driverRow.Controls.Add(new Label { Text = driver.Title, AutoSize = true, Font = Theme.Strong, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(6), 0, 0) });
        Row(L.T("about.driver", "Драйвер"), driverRow);
        Row(L.T("about.theme", "Тема"), Value(ThemeStore.Current.Title));
        Row(L.T("about.language", "Язык"), Value(L.Available.FirstOrDefault(l => l.Code == L.Code)?.Name ?? L.T("menu.language.builtin", "Русский (встроенный)")));
        details.Controls.Add(table);
        _body.Controls.Add(Fit(details, table, inner));

        // Рамка с благодарностями: откуда взяты протоколы плат.
        var creditsFrame = Frame(inner);
        var credits = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = creditsFrame.Fill, Dock = DockStyle.Top };
        credits.Controls.Add(new Label
        {
            Text = L.T("about.credits", "Протоколы плат восстановлены по открытым проектам:"),
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(inner - Theme.S(32), 0),
            Margin = new Padding(0, 0, 0, Theme.S(4)),
        });
        foreach (var (name, url) in new[]
                 {
                     ("kriomant/ch57x-keyboard-tool", "https://github.com/kriomant/ch57x-keyboard-tool"),
                     ("rOzzy1987/MacroPad", "https://github.com/rOzzy1987/MacroPad"),
                 })
            credits.Controls.Add(Link(name, inner - Theme.S(32), () => Open(url, null)));
        creditsFrame.Controls.Add(credits);
        _body.Controls.Add(Fit(creditsFrame, credits, inner));

        _buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        var close = ThemedDialog.MakeButton(L.T("button.close", "Закрыть"), primary: true);
        close.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        _buttons.Controls.Add(close);
        var site = ThemedDialog.MakeButton(L.T("about.github", "Страница на GitHub"), primary: false);
        site.Click += (_, _) => Open(Branding.Website, null);
        _buttons.Controls.Add(site);

        _card.Controls.Add(_body);
        _card.Controls.Add(header);
        Controls.Add(_card);
        Controls.Add(_buttons);
        Theme.Style(this);
    }

    // Под карточкой — снимок окна, приглушённый цветом темы; у темы со своим фоном страница прозрачна и виден он.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (BackColor.A < 255)
        {
            base.OnPaintBackground(e);
            return;
        }
        var g = e.Graphics;
        g.Clear(Theme.Window);
        if (_backdrop == null) return;
        g.DrawImageUnscaled(_backdrop, 0, 0);
        using var veil = new SolidBrush(Color.FromArgb(255 - BackdropOpacity, Theme.Window));
        g.FillRectangle(veil, ClientRectangle);
    }

    // Карточка по высоте содержимого (но не выше окна), вместе с кнопками — по центру страницы.
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_card == null || _buttons == null) return;
        int gap = Theme.S(14);
        int content = _body.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + _body.Padding.Vertical + Theme.S(12);
        int wanted = Theme.S(150) + content + _card.Padding.Vertical;
        int room = ClientSize.Height - _buttons.Height - gap - Theme.S(24);
        _card.Height = Math.Max(Theme.S(240), Math.Min(wanted, room));
        int top = Math.Max(0, (ClientSize.Height - _card.Height - gap - _buttons.Height) / 2);
        _card.Location = new Point(Math.Max(0, (ClientSize.Width - _card.Width) / 2), top);
        _buttons.Location = new Point(_card.Right - _buttons.Width, _card.Bottom + gap);
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

    static RoundPanel Frame(int width) => new()
    {
        Fill = Theme.Blend(Theme.Surface, Theme.Window, 0.55f),
        Line = Theme.Border,
        Radius = Theme.S(12),
        Width = width,
        Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(10)),
        Margin = new Padding(0, 0, 0, Theme.S(12)),
    };

    // Высота рамки — по содержимому, раскладываемому в её ширину.
    static RoundPanel Fit(RoundPanel frame, Control content, int width)
    {
        frame.Height = content.GetPreferredSize(new Size(width - frame.Padding.Horizontal, 0)).Height + frame.Padding.Vertical;
        return frame;
    }

    static Label Value(string text) => new() { Text = text, AutoSize = true, Font = Theme.Strong, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(5), 0, Theme.S(5)) };

    static LinkLabel Link(string text, int maxWidth, Action click)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentDark,
            VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            MaximumSize = new Size(maxWidth, 0),
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
        if (disposing)
        {
            foreach (var image in _images) image.Dispose();
            _backdrop?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Панель-рамка со скруглёнными углами; за углами виден фон родителя.</summary>
    sealed class RoundPanel : Panel
    {
        public Color Fill { get; init; }
        public Color Line { get; init; }
        public int Radius { get; init; }

        public RoundPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
            using (var brush = new SolidBrush(Fill)) g.FillPath(brush, path);
            using (var pen = new Pen(Line)) g.DrawPath(pen, path);
        }
    }

    /// <summary>Шапка: градиент акцента со скруглёнными верхними углами, крупная иконка, название и версия.</summary>
    sealed class Banner : Control
    {
        readonly Image _icon;
        readonly int _radius;

        public Banner(int radius)
        {
            _radius = radius;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            using var stream = new MemoryStream(Theme.BuildIconBytes());
            using var icon = new Icon(stream, 64, 64);
            _icon = icon.ToBitmap();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.Scale;
            // Скругление только сверху: нижние углы уходят за край шапки.
            using var shape = Theme.Rounded(new RectangleF(0, 0, Width, Height + _radius * 2), _radius);
            g.SetClip(shape);
            using (var brush = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.AccentPressed, 25f))
                g.FillRectangle(brush, ClientRectangle);
            // Декоративные круги в духе ручки устройства.
            using (var pen = new Pen(Color.FromArgb(40, Theme.OnAccent), 10 * s))
            {
                g.DrawEllipse(pen, Width - 150 * s, -60 * s, 220 * s, 220 * s);
                g.DrawEllipse(pen, Width - 90 * s, 40 * s, 120 * s, 120 * s);
            }
            g.ResetClip();
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
