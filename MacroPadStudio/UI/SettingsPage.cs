using System.Diagnostics;
using OshiMakiKey.Agent;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Страница настроек внутри главного окна: разделы слева, содержимое справа. Изменения применяются по «Готово»
/// (событие <see cref="Done"/>), «Отмена» — <see cref="Cancelled"/>. Служба OMK_Studio-Service управляется сразу кнопками.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    readonly AppSettings _settings;
    readonly (ushort Vid, ushort Pid)? _connected;
    readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly Label _pageTitle = new();
    readonly List<NavItem> _nav = new();
    readonly List<Image> _images = new();
    readonly ToolTip _tips = new();

    string _theme;
    string _language;
    string? _driverId;
    int _layerCount;
    bool _autostart, _tray, _confirm, _noRepeat;
    string? _servicePreset;
    Label? _serviceStatus;

    /// <summary>Тема или язык изменились — главное окно перестроит содержимое.</summary>
    public bool AppearanceChanged { get; private set; }
    public bool DriverChanged { get; private set; }
    public bool RerunSetup { get; private set; }
    public bool ShowTour { get; private set; }

    readonly int _startPage;

    public event EventHandler? Done;
    public event EventHandler? Cancelled;

    public SettingsPage(AppSettings settings, (ushort Vid, ushort Pid)? connected, int startPage = 0)
    {
        _startPage = startPage;
        _settings = settings;
        _connected = connected;
        _theme = ThemeStore.Current.Id;
        _language = L.Code;
        _driverId = settings.DriverId;
        _layerCount = settings.LayerCount;
        _autostart = Autostart.IsEnabled;
        _tray = settings.MinimizeToTray;
        _confirm = settings.ConfirmOverwrite;
        _noRepeat = settings.NoKeyRepeat;
        _servicePreset = settings.ServicePreset;

        Font = Theme.Body;
        BackColor = Theme.Window;
        AutoScaleMode = AutoScaleMode.None;
        Dock = DockStyle.Fill;

        // Разделы
        var sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(230), BackColor = Theme.Surface, Padding = new Padding(Theme.S(12), Theme.S(16), Theme.S(12), Theme.S(12)) };
        var navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        AddNav(navList, "", "🎨", L.T("settings.appearance", "Оформление"), BuildAppearance);
        AddNav(navList, "", "🌐", L.T("settings.language", "Язык"), BuildLanguage);
        AddNav(navList, "", "⌨", L.T("settings.device", "Устройство"), BuildDevice);
        AddNav(navList, "", "⇄", L.T("settings.keys", "Клавиши"), BuildKeys);
        AddNav(navList, "", "⚙", L.T("settings.service", "Фоновая служба"), BuildService);
        AddNav(navList, "", "⏻", L.T("settings.startup", "Запуск"), BuildStartup);
        AddNav(navList, "", "📁", L.T("settings.files", "Файлы и справка"), BuildFiles);
        var sidebarTitle = new Label
        {
            Text = L.T("settings.title", "Настройки"),
            Font = Theme.Title,
            ForeColor = Theme.Text,
            Dock = DockStyle.Top,
            Height = Theme.S(48),
            Padding = new Padding(Theme.S(6), 0, 0, 0),
        };
        sidebar.Controls.Add(navList);
        sidebar.Controls.Add(sidebarTitle);
        var sidebarLine = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border };

        // Содержимое
        _pageTitle.Font = Theme.Title;
        _pageTitle.ForeColor = Theme.Text;
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.Height = Theme.S(56);
        _pageTitle.Padding = new Padding(Theme.S(28), Theme.S(16), 0, 0);
        _content.Padding = new Padding(Theme.S(28), Theme.S(4), Theme.S(28), Theme.S(16));
        _content.BackColor = Theme.Window;

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(60),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(20), Theme.S(12)),
            BackColor = Theme.Surface,
        };
        var cancel = ThemedDialog.MakeButton(L.T("button.cancel", "Отмена"), primary: false);
        cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        var done = ThemedDialog.MakeButton(L.T("button.done", "Готово"), primary: true);
        done.Click += (_, _) => Apply();
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(done);
        var bottomLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };

        Controls.Add(_content);
        Controls.Add(_pageTitle);
        Controls.Add(bottomLine);
        Controls.Add(bottom);
        Controls.Add(sidebarLine);
        Controls.Add(sidebar);
        ForeColor = Theme.Text;
        Theme.Style(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Раздел строится, когда размеры окна уже известны: карточки раскладываются по ширине.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Select(_nav[Math.Clamp(_startPage, 0, _nav.Count - 1)]);
    }

    // ───────────────────────── Разделы ─────────────────────────

    void AddNav(FlowLayoutPanel list, string glyph, string fallback, string text, Action build)
    {
        var item = new NavItem(Theme.IconGlyph(glyph, fallback), text) { Width = Theme.S(204), Height = Theme.S(42), Tag = build };
        item.Click += (_, _) => Select(item);
        _nav.Add(item);
        list.Controls.Add(item);
    }

    void Select(NavItem item)
    {
        foreach (var nav in _nav) nav.Selected = nav == item;
        _pageTitle.Text = item.Caption;
        _content.SuspendLayout();
        foreach (var control in _content.Controls.Cast<Control>().ToList()) control.Dispose();
        _content.Controls.Clear();
        _serviceStatus = null;
        ((Action)item.Tag!)();
        Theme.Style(_content);
        _content.ResumeLayout(true);
        _content.AutoScrollPosition = Point.Empty;
    }

    FlowLayoutPanel Page()
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Window,
        };
        _content.Controls.Add(page);
        return page;
    }

    int ContentWidth => Math.Max(Theme.S(360), _content.ClientSize.Width - _content.Padding.Horizontal - Theme.S(20));

    Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        MaximumSize = new Size(ContentWidth, 0),
        Margin = new Padding(0, 0, 0, Theme.S(14)),
    };

    Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Subtitle,
        ForeColor = Theme.Text,
        Margin = new Padding(0, Theme.S(10), 0, Theme.S(8)),
    };

    FlowLayoutPanel Cards()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            MaximumSize = new Size(ContentWidth + Theme.S(12), 0),
            BackColor = Theme.Window,
            Margin = new Padding(0),
        };
    }

    CheckBox Option(FlowLayoutPanel page, string text, string hint, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Text = text, AutoSize = true, Checked = value, Font = Theme.Strong, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)) };
        box.CheckedChanged += (_, _) => changed(box.Checked);
        page.Controls.Add(box);
        var label = Hint(hint);
        label.Margin = new Padding(Theme.S(22), 0, 0, Theme.S(12));
        page.Controls.Add(label);
        return box;
    }

    Button ActionButton(string text, EventHandler click, bool primary = false, bool enabled = true)
    {
        var button = ThemedDialog.MakeButton(text, primary);
        button.Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
        button.Enabled = enabled;
        button.Click += click;
        return button;
    }

    Image Keep(Image image)
    {
        _images.Add(image);
        return image;
    }

    void BuildAppearance()
    {
        var page = Page();
        page.Controls.Add(Hint(L.T("settings.appearance.hint", "Тема меняет цвета окна, рисунка платы и всех диалогов. Темы лежат в папке theme — их можно править и добавлять свои.")));
        var cards = Cards();
        var themes = ThemeStore.All.Count > 0 ? ThemeStore.All : new[] { ThemeStore.Current };
        foreach (var theme in themes)
        {
            var card = new SelectCard
            {
                Text = theme.Title,
                Picture = Keep(ThemeStore.Preview(theme, Theme.S(160), Theme.S(100))),
                PictureOnTop = true,
                Size = new Size(Theme.S(180), Theme.S(150)),
                Selected = theme.Id == _theme,
            };
            card.Click += (_, _) =>
            {
                _theme = theme.Id;
                foreach (var other in cards.Controls.OfType<SelectCard>()) other.Selected = other == card;
            };
            cards.Controls.Add(card);
        }
        page.Controls.Add(cards);
    }

    void BuildLanguage()
    {
        var page = Page();
        page.Controls.Add(Hint(L.T("settings.language.hint", "Языковые пакеты лежат в папке language. Перевод применится после «Готово».")));
        var cards = Cards();
        var languages = L.Available.Count > 0 ? L.Available : new[] { new LanguageInfo(L.BuiltInCode, L.T("menu.language.builtin", "Русский (встроенный)"), "") };
        foreach (var language in languages)
        {
            var card = new SelectCard
            {
                Text = language.Name,
                Subtitle = language.Code,
                Size = new Size(Theme.S(250), Theme.S(60)),
                Selected = language.Code == _language,
            };
            card.Click += (_, _) =>
            {
                _language = language.Code;
                foreach (var other in cards.Controls.OfType<SelectCard>()) other.Selected = other == card;
            };
            cards.Controls.Add(card);
        }
        page.Controls.Add(cards);
    }

    void BuildDevice()
    {
        var page = Page();
        page.Controls.Add(Hint(L.T("settings.device.hint", "Драйвер — описание платы: её USB ID, формат записи и раскладка. Обычно он подбирается сам; если плата отличается от нарисованной, выберите подходящий вручную.")));

        var list = Cards();
        int width = Math.Min(ContentWidth, Theme.S(640));
        var compatible = _connected is { } usb ? DriverStore.ForUsb(usb.Vid, usb.Pid).Select(d => d.Id).ToHashSet() : new HashSet<string>();
        string detected = DriverStore.Find(_settings.DetectedDriverId)?.Title ?? DriverStore.Default.Title;

        SelectCard? layersOwner = null;
        var layers = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Window, Margin = new Padding(0, Theme.S(6), 0, 0) };

        void Mark(SelectCard selected)
        {
            foreach (var other in list.Controls.OfType<SelectCard>()) other.Selected = other == selected;
            layersOwner = selected;
            var driver = _driverId == null ? DriverStore.Find(_settings.DetectedDriverId) ?? DriverStore.Default : DriverStore.Find(_driverId)!;
            layers.Visible = Execution.SupportsLayers(driver.Protocol);
        }

        var auto = new SelectCard
        {
            Text = L.T("menu.driver.auto", "Определять автоматически"),
            Subtitle = L.F("settings.device.autoNow", "Сейчас: {0}", detected),
            Size = new Size(width, Theme.S(64)),
            Selected = _driverId == null,
        };
        auto.Click += (_, _) => { _driverId = null; Mark(auto); };
        list.Controls.Add(auto);

        foreach (var driver in DriverStore.All.OrderByDescending(d => compatible.Contains(d.Id)))
        {
            string usbIds = string.Join(", ", driver.Usb);
            string fits = compatible.Contains(driver.Id) ? "  ·  " + L.T("settings.device.fits", "подходит к подключённой плате") : "";
            var card = new SelectCard
            {
                Text = driver.Title,
                Subtitle = $"{UiText.LayoutText(driver)}  ·  {UiText.ProtocolName(driver.Protocol)}  ·  USB {usbIds}{fits}",
                Picture = Keep(DriverIcon.Draw(driver, Theme.S(84), Theme.S(56))),
                Size = new Size(width, Theme.S(76)),
                Selected = driver.Id == _driverId,
            };
            if (driver.Note is { Length: > 0 } note) _tips.SetToolTip(card, note);
            string id = driver.Id;
            card.Click += (_, _) => { _driverId = id; Mark(card); };
            list.Controls.Add(card);
        }
        page.Controls.Add(list);

        layers.Controls.Add(new Label { Text = L.T("menu.layers", "Количество слоёв") + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(6), Theme.S(10), 0) });
        var layerBox = new ThemedComboBox { Width = Theme.S(80) };
        for (int n = 1; n <= Profile.MaxLayers; n++) layerBox.Items.Add(n.ToString());
        layerBox.SelectedIndex = Math.Clamp(_layerCount, 1, Profile.MaxLayers) - 1;
        layerBox.SelectedIndexChanged += (_, _) => _layerCount = layerBox.SelectedIndex + 1;
        layers.Controls.Add(layerBox);
        page.Controls.Add(layers);
        Mark(list.Controls.OfType<SelectCard>().First(c => c.Selected));
        _ = layersOwner;
    }

    void BuildKeys()
    {
        var page = Page();
        Option(page, L.T("settings.noRepeat", "Не повторять сочетание, пока клавиша зажата"),
            L.T("settings.noRepeat.hint", "Пока клавиша зажата, плата сама повторяет нажатие примерно 9 раз в секунду, и записанное в неё сочетание срабатывает снова и снова. С этой настройкой сочетания выполняет программа — один раз на нажатие, сколько бы клавишу ни держали. Для этого должна работать программа или фоновая служба. Мультимедиа и системные действия программа и так выполняет один раз; громкость и яркость при удержании повторяются."),
            _noRepeat, value => _noRepeat = value);
        Option(page, L.T("settings.confirmOverwrite", "Спрашивать перед перезаписью при перетаскивании"),
            L.T("settings.confirmOverwrite.hint", "Если перетащить действие на кнопку, где уже что-то назначено, программа предупредит. Отключается и флажком «Больше не показывать» в самом предупреждении."),
            _confirm, value => _confirm = value);
        page.Controls.Add(Hint(L.T("settings.keys.drag", "Перетащите клавишу на другую, чтобы перенести действие; с зажатым Ctrl — скопировать.")));
    }

    void BuildService()
    {
        var page = Page();
        page.Controls.Add(Hint(L.F("settings.service.hint",
            "{0} выполняет громкость, треки и другие действия кнопок, когда окно программы закрыто. Она запускается вместе с Windows, не показывает окон и уступает клавиши, пока открыта программа.",
            OmkService.Name)));
        // Частый вопрос: «службы нет в services.msc». Это сделано намеренно — объясняем, где её искать.
        page.Controls.Add(Hint(L.F("settings.service.where",
            "Это не служба из списка «Службы» Windows: такие службы работают в отдельном сеансе без доступа к клавиатуре, звуку и окнам. {0} — фоновый процесс в автозагрузке: «Диспетчер задач → Автозагрузка», процесс OshiMakiKeyStudio.exe.",
            OmkService.Name)));

        _serviceStatus =new Label { AutoSize = true, Font = Theme.Strong, Margin = new Padding(0, 0, 0, Theme.S(14)) };
        page.Controls.Add(_serviceStatus);
        UpdateServiceStatus();

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Window, Margin = new Padding(0, 0, 0, Theme.S(10)) };
        row.Controls.Add(ActionButton(L.T("settings.service.install", "Установить и запустить"), (_, _) => ServiceCommand(OmkService.Install), primary: true));
        row.Controls.Add(ActionButton(L.T("settings.service.reinstall", "Переустановить"), (_, _) => ServiceCommand(OmkService.Reinstall)));
        row.Controls.Add(ActionButton(L.T("settings.service.remove", "Удалить службу"), (_, _) => ServiceCommand(OmkService.Remove)));
        page.Controls.Add(row);

        page.Controls.Add(Section(L.T("settings.service.preset", "Пресет службы")));
        page.Controls.Add(Hint(L.T("settings.service.preset.hint", "Служба помнит выбранный пресет и выполняет его действия. Обычно это тот, что записан в клавиатуру.")));
        var presets = new ThemedComboBox { Width = Theme.S(300), Margin = new Padding(0, 0, 0, Theme.S(10)) };
        string active = L.T("settings.service.active", "Активный пресет");
        presets.Items.Add(active);
        foreach (var name in PresetStore.Names()) presets.Items.Add(name);
        presets.SelectedItem = _servicePreset != null && presets.Items.Contains(_servicePreset) ? _servicePreset : active;
        presets.SelectedIndexChanged += (_, _) => _servicePreset = presets.SelectedIndex <= 0 ? null : presets.SelectedItem as string;
        page.Controls.Add(presets);
    }

    void ServiceCommand(Action command)
    {
        try
        {
            UseWaitCursor = true;
            command();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            ThemedDialog.Show(this, Branding.AppName, OmkService.Name, ex.Message, new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
        }
        finally
        {
            UseWaitCursor = false;
            UpdateServiceStatus();
        }
    }

    void UpdateServiceStatus()
    {
        if (_serviceStatus == null) return;
        bool installed = OmkService.IsInstalled, running = OmkService.IsRunning;
        _serviceStatus.Text = installed && running ? L.T("settings.service.running", "● Служба установлена и работает")
            : installed ? L.T("settings.service.stopped", "● Служба установлена, но не запущена")
            : L.T("settings.service.absent", "○ Служба не установлена");
        _serviceStatus.ForeColor = installed && running ? Theme.Success : installed ? Theme.Warning : Theme.Muted;
    }

    void BuildStartup()
    {
        var page = Page();
        Option(page, L.T("menu.autostart", "Запускать вместе с Windows"),
            L.T("settings.autostart.hint", "Программа будет стартовать свёрнутой в трей. Если установлена фоновая служба, это не обязательно."),
            _autostart, value => _autostart = value);
        Option(page, L.T("menu.minimizeToTray", "Сворачивать в трей при закрытии"),
            L.T("settings.tray.hint", "Крестик прячет окно в трей, программа продолжает выполнять действия. Выйти — правой кнопкой по значку."),
            _tray, value => _tray = value);
    }

    void BuildFiles()
    {
        var page = Page();
        page.Controls.Add(Section(L.T("settings.files.folders", "Папки программы")));
        var folders = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(ContentWidth, 0), BackColor = Theme.Window };
        folders.Controls.Add(ActionButton(L.T("settings.folder.presets", "Пресеты"), (_, _) => OpenFolder(AppPaths.Presets)));
        folders.Controls.Add(ActionButton(L.T("settings.folder.drivers", "Драйверы"), (_, _) => OpenFolder(AppPaths.Drivers)));
        folders.Controls.Add(ActionButton(L.T("settings.folder.themes", "Темы"), (_, _) => OpenFolder(AppPaths.Themes)));
        folders.Controls.Add(ActionButton(L.T("settings.folder.languages", "Языки"), (_, _) => OpenFolder(AppPaths.Languages)));
        folders.Controls.Add(ActionButton(L.T("settings.folder.data", "Данные (user_data)"), (_, _) => OpenFolder(AppPaths.UserData)));
        page.Controls.Add(folders);

        page.Controls.Add(Section(L.T("settings.files.updates", "Обновления")));
        var updates = ActionButton(L.T("menu.updates", "Проверить обновления и файлы"), (_, _) => { }, enabled: false);
        _tips.SetToolTip(updates, L.T("menu.updates.soon", "Скоро"));
        page.Controls.Add(updates);
        page.Controls.Add(Hint(L.T("settings.updates.hint", "Проверка новой версии и целостности файлов программы появится в следующих версиях.")));

        page.Controls.Add(Section(L.T("settings.files.help", "Знакомство с программой")));
        var help = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Window };
        help.Controls.Add(ActionButton(L.T("menu.howItWorks", "Как это работает"), (_, _) => { ShowTour = true; Apply(); }));
        help.Controls.Add(ActionButton(L.T("menu.setup", "Мастер первого запуска…"), (_, _) => { RerunSetup = true; Apply(); }));
        page.Controls.Add(help);
    }

    void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ThemedDialog.Show(this, Branding.AppName, ex.Message, path, new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
        }
    }

    // ───────────────────────── Применение ─────────────────────────

    void Apply()
    {
        AppearanceChanged = _theme != ThemeStore.Current.Id || _language != L.Code;
        if (_theme != ThemeStore.Current.Id) _settings.Theme = _theme;
        if (_language != L.Code) _settings.Language = _language;

        DriverChanged = _driverId != _settings.DriverId || _layerCount != _settings.LayerCount || _noRepeat != _settings.NoKeyRepeat;
        _settings.DriverId = _driverId;
        _settings.LayerCount = _layerCount;
        _settings.NoKeyRepeat = _noRepeat;
        _settings.MinimizeToTray = _tray;
        _settings.ConfirmOverwrite = _confirm;
        _settings.ServicePreset = _servicePreset;
        if (_autostart != Autostart.IsEnabled)
        {
            try { Autostart.Set(_autostart); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                ThemedDialog.Show(this, Branding.AppName, L.T("menu.autostart", "Запускать вместе с Windows"), ex.Message,
                    new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
            }
        }
        Done?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _images) image.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Пункт списка разделов: значок и название, выбранный подсвечен акцентом.</summary>
    sealed class NavItem : Control
    {
        readonly string _glyph;
        bool _selected, _hover;

        public string Caption { get; }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public NavItem(string glyph, string caption)
        {
            _glyph = glyph;
            Caption = caption;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, Theme.S(4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = Theme.Scale;
            if (_selected || _hover)
            {
                using var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 8 * s);
                using var brush = new SolidBrush(_selected ? Theme.AccentSoft : Theme.Hover);
                g.FillPath(brush, path);
            }
            if (_selected)
            {
                using var bar = Theme.Rounded(new RectangleF(0, 9 * s, 4 * s, Height - 18 * s), 2 * s);
                using var brush = new SolidBrush(Theme.Accent);
                g.FillPath(brush, bar);
            }
            var color = _selected ? Theme.AccentText : Theme.Text;
            TextRenderer.DrawText(g, _glyph, Theme.IconFont, new Rectangle((int)(12 * s), 0, (int)(28 * s), Height), _selected ? Theme.Accent : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Caption, _selected ? Theme.Strong : Theme.Body, new Rectangle((int)(48 * s), 0, Width - (int)(52 * s), Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    }
}
