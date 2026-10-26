using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Мастер первого запуска — страница главного окна: язык → тема → плата. Язык и тема применяются сразу,
/// мастер перерисовывается в них же (<see cref="AppearanceChanged"/> — чтобы окно перекрасило заголовок).
/// По «Готово» — <see cref="Finished"/>; дальше окно показывает программу и тур по ней.
/// </summary>
internal sealed class SetupPage : UserControl
{
    readonly AppSettings _settings;
    readonly List<Image> _images = new();
    int _page;

    public event EventHandler? Finished;
    public event EventHandler? AppearanceChanged;

    /// <summary>Пользователь нажал «Проверить запись…» — окно откроет страницу проверки поверх мастера.</summary>
    public event EventHandler? WriteCheckRequested;

    public SetupPage(AppSettings settings, int page = 0)
    {
        _settings = settings;
        _page = page;
        AutoScaleMode = AutoScaleMode.None;
        Dock = DockStyle.Fill;
    }

    // Строим при первом показе: ширина страницы к этому моменту известна.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Render();
    }

    /// <summary>Вернулись со страницы проверки записи — перерисовать (могли смениться номера ручки).</summary>
    public void Rebuild() => Rerender();

    // Перерисовка из обработчика щелчка: сам щёлкнутый элемент при этом удаляется, поэтому — после выхода из обработчика.
    void Rerender() => BeginInvoke(new Action(Render));

    // ───────────────────────── Отрисовка ─────────────────────────

    void Render()
    {
        SuspendLayout();
        foreach (var control in Controls.Cast<Control>().ToList()) control.Dispose();
        Controls.Clear();
        foreach (var image in _images) image.Dispose();
        _images.Clear();

        Font = Theme.Body;
        BackColor = Theme.Window;

        var steps = new[] { L.T("setup.step.language", "Язык"), L.T("setup.step.theme", "Тема"), L.T("setup.step.device", "Устройство") };
        var header = new StepsHeader(steps, _page) { Dock = DockStyle.Top, Height = Theme.S(64) };

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Theme.S(40), Theme.S(18), Theme.S(40), Theme.S(10)), BackColor = Theme.Window };
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Window,
        };
        content.Controls.Add(page);
        int width = Math.Max(Theme.S(600), ClientSize.Width - Theme.S(100));
        switch (_page)
        {
            case 0: BuildLanguage(page, width); break;
            case 1: BuildTheme(page, width); break;
            default: BuildDevice(page, width); break;
        }

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(66),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(Theme.S(28), Theme.S(14), Theme.S(28), Theme.S(14)),
            BackColor = Theme.Surface,
        };
        var next = ThemedDialog.MakeButton(_page == 2 ? L.T("setup.finish", "Готово") : L.T("setup.next", "Далее"), primary: true);
        next.MinimumSize = new Size(Theme.S(130), Theme.S(36));
        next.Click += (_, _) => Next();
        bottom.Controls.Add(next);
        if (_page > 0)
        {
            var back = ThemedDialog.MakeButton(L.T("tour.back", "Назад"), primary: false);
            back.Click += (_, _) => { _page--; Rerender(); };
            bottom.Controls.Add(back);
        }

        Controls.Add(content);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
        Controls.Add(header);
        ForeColor = Theme.Text;
        Theme.Style(this);
        ResumeLayout(true);
        next.Focus();
    }

    static Label Title(string text) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, Theme.S(6)) };

    static Label Hint(string text, int width) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        MaximumSize = new Size(width, 0),
        Margin = new Padding(0, 0, 0, Theme.S(18)),
    };

    static FlowLayoutPanel Cards(int width) => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        MaximumSize = new Size(width + Theme.S(12), 0),
        BackColor = Theme.Window,
        Margin = new Padding(0),
    };

    void BuildLanguage(FlowLayoutPanel page, int width)
    {
        page.Controls.Add(Title(L.T("setup.language.title", "Выберите язык")));
        page.Controls.Add(Hint(L.T("setup.language.hint", "Язык можно сменить позже в настройках. Языковые пакеты лежат в папке language."), width));
        var cards = Cards(width);
        var languages = L.Available.Count > 0 ? L.Available : new[] { new LanguageInfo(L.BuiltInCode, L.T("menu.language.builtin", "Русский (встроенный)"), "") };
        foreach (var language in languages)
        {
            var card = new SelectCard
            {
                Text = language.Name,
                Subtitle = language.Code,
                Size = new Size(Theme.S(248), Theme.S(62)),
                Selected = language.Code == L.Code,
            };
            card.Click += (_, _) =>
            {
                _settings.Language = language.Code;
                L.Load(language.Code);
                KeyCatalog.Rebuild();
                MediaCatalog.Rebuild();
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
                Rerender();
            };
            cards.Controls.Add(card);
        }
        page.Controls.Add(cards);
    }

    void BuildTheme(FlowLayoutPanel page, int width)
    {
        page.Controls.Add(Title(L.T("setup.theme.title", "Выберите тему")));
        page.Controls.Add(Hint(L.T("setup.theme.hint", "Тема меняет цвета всей программы. Выбор применяется сразу — этот мастер тоже перекрасится."), width));
        var cards = Cards(width);
        var themes = ThemeStore.All.Count > 0 ? ThemeStore.All : new[] { ThemeStore.Current };
        foreach (var theme in themes)
        {
            var preview = ThemeStore.Preview(theme, Theme.S(160), Theme.S(100));
            _images.Add(preview);
            var card = new SelectCard
            {
                Text = theme.Title,
                Picture = preview,
                PictureOnTop = true,
                Size = new Size(Theme.S(184), Theme.S(150)),
                Selected = theme.Id == ThemeStore.Current.Id,
            };
            card.Click += (_, _) =>
            {
                _settings.Theme = theme.Id;
                ThemeStore.Apply(theme.Id);
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
                Rerender();
            };
            cards.Controls.Add(card);
        }
        page.Controls.Add(cards);
    }

    void BuildDevice(FlowLayoutPanel page, int width)
    {
        page.Controls.Add(Title(L.T("setup.device.title", "Ваше устройство")));
        var config = MacroPadDevice.FindConfigInterface();
        var detected = config != null ? MacroPadDevice.DetectDriver() : null;
        if (detected != null) _settings.DetectedDriverId = detected.Id;

        var status = new Label
        {
            AutoSize = true,
            Font = Theme.Strong,
            Margin = new Padding(0, 0, 0, Theme.S(6)),
            Text = config != null
                ? L.F("setup.device.found", "● Найдена плата {0:X4}:{1:X4}", config.VendorId, config.ProductId)
                : L.T("setup.device.notFound", "○ Плата не найдена — подключите её USB-кабелем и нажмите «Обновить»"),
            ForeColor = config != null ? Theme.Success : Theme.Warning,
        };
        page.Controls.Add(status);
        page.Controls.Add(Hint(L.T("setup.device.hint", "Выберите плату, которая выглядит как ваша. Если не уверены, оставьте «Определять автоматически» — драйвер можно сменить позже в настройках."), width));

        var compatible = config != null ? DriverStore.ForUsb(config.VendorId, config.ProductId) : DriverStore.All.ToList();
        var cards = Cards(width);
        int cardWidth = Math.Min(width, Theme.S(700));
        var auto = new SelectCard
        {
            Text = L.T("menu.driver.auto", "Определять автоматически"),
            Subtitle = detected != null ? L.F("settings.device.autoNow", "Сейчас: {0}", detected.Title) : L.T("setup.device.autoHint", "Драйвер подберётся при подключении платы"),
            Size = new Size(cardWidth, Theme.S(64)),
            Selected = _settings.DriverId == null,
        };
        auto.Click += (_, _) => { _settings.DriverId = null; Rerender(); };
        cards.Controls.Add(auto);
        foreach (var driver in compatible)
        {
            var icon = DriverIcon.Draw(driver, Theme.S(84), Theme.S(56));
            _images.Add(icon);
            var card = new SelectCard
            {
                Text = driver.Title,
                Subtitle = $"{UiText.LayoutText(driver)}  ·  {UiText.ProtocolName(driver.Protocol)}",
                Picture = icon,
                Size = new Size(cardWidth, Theme.S(76)),
                Selected = _settings.DriverId == driver.Id,
            };
            string id = driver.Id;
            card.Click += (_, _) => { _settings.DriverId = id; Rerender(); };
            cards.Controls.Add(card);
        }
        page.Controls.Add(cards);

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Window, Margin = new Padding(0, Theme.S(6), 0, 0) };
        var refresh = ThemedDialog.MakeButton(L.T("setup.device.refresh", "Обновить"), primary: false);
        refresh.Margin = new Padding(0, 0, Theme.S(8), 0);
        refresh.Click += (_, _) => Rerender();
        row.Controls.Add(refresh);
        var effective = _settings.EffectiveDriver;
        if (config != null && effective.Protocol == ProtocolMode.Direct)
        {
            var check = ThemedDialog.MakeButton(L.T("menu.wizard", "Проверить запись…"), primary: false);
            check.Margin = new Padding(0);
            check.Click += (_, _) =>
            {
                WriteCheckRequested?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(check);
        }
        page.Controls.Add(row);
    }

    void Next()
    {
        if (_page < 2)
        {
            _page++;
            Rerender();
            return;
        }
        _settings.SetupDone = true;
        _settings.TourDone = false;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var image in _images) image.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Шапка мастера: шаги с номерами, пройденные и текущий подсвечены.</summary>
    sealed class StepsHeader : Control
    {
        readonly string[] _steps;
        readonly int _current;

        public StepsHeader(string[] steps, int current)
        {
            _steps = steps;
            _current = current;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = Theme.Scale;
            using (var brush = new SolidBrush(Theme.Surface)) g.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            float x = 40 * s, y = Height / 2f;
            for (int i = 0; i < _steps.Length; i++)
            {
                bool done = i < _current, current = i == _current;
                var circle = new RectangleF(x, y - 13 * s, 26 * s, 26 * s);
                using (var brush = new SolidBrush(done || current ? Theme.Accent : Theme.Badge)) g.FillEllipse(brush, circle);
                TextRenderer.DrawText(g, done ? "✓" : (i + 1).ToString(), Theme.Strong, Rectangle.Round(circle), done || current ? Theme.OnAccent : Theme.BadgeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x = circle.Right + 8 * s;
                var size = TextRenderer.MeasureText(_steps[i], current ? Theme.Strong : Theme.Body);
                TextRenderer.DrawText(g, _steps[i], current ? Theme.Strong : Theme.Body, new Point((int)x, (int)(y - size.Height / 2f)),
                    current ? Theme.Text : Theme.Muted, TextFormatFlags.NoPrefix);
                x += size.Width + 12 * s;
                if (i < _steps.Length - 1)
                {
                    using var pen = new Pen(done ? Theme.Accent : Theme.Border, 2 * s);
                    g.DrawLine(pen, x, y, x + 40 * s, y);
                    x += 52 * s;
                }
            }
        }
    }
}
