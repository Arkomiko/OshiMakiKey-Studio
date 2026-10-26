using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Что перенести в пересозданное окно (после смены темы или языка): положение, выбранную кнопку и слой.</summary>
internal sealed record ReopenState(Rectangle Bounds, FormWindowState State, ControlId Selected, int Layer);

/// <summary>Главное окно: схема устройства, редактор действия, пресеты, запись в устройство, значок в трее и фоновый агент.</summary>
internal sealed class MainForm : Form
{
    const string AppName = Branding.AppName;

    readonly AppSettings _settings;
    readonly EventWaitHandle? _showEvent;
    readonly string? _snapshotPath;
    readonly bool _interactive;
    bool _allowVisible;
    bool _exiting;
    bool _writing;
    bool _suppressLayerEvent;
    bool _suppressPresetEvent;
    int _layer;
    HidDeviceInfo? _device;
    HashSet<int> _registeredSlots = new();
    List<int> _failedSlots = new();
    RegisteredWaitHandle? _showWait;
    MonitorForm? _monitor;

    readonly DevicePanel _pad = new();
    readonly BindingEditor _editor = new();
    readonly NoticeBar _notices = new();
    readonly StatusDot _deviceDot = new();
    readonly Label _deviceText = new();
    readonly Label _dirtyLabel = new();
    readonly Label _presetLabel = new();
    readonly ThemedComboBox _presetBox = new();
    readonly Label _layerLabel = new();
    readonly ThemedComboBox _layerBox = new();
    readonly Button _writeButton = new();
    readonly ToolStripStatusLabel _status = new();
    readonly ToolStripStatusLabel _driverStatus = new();
    readonly ToolStripStatusLabel _agentStatus = new();
    readonly NotifyIcon _tray = new();
    readonly HotkeyAgent? _agent;
    readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 700 };
    readonly ToolStripMenuItem _autostartItem = new(L.T("menu.autostart", "Запускать вместе с Windows"));
    readonly ToolStripMenuItem _trayItem = new(L.T("menu.minimizeToTray", "Сворачивать в трей при закрытии"));
    readonly ToolStripMenuItem _layersMenu = new(L.T("menu.layers", "Количество слоёв"));
    readonly ToolStripMenuItem _deletePresetItem = new(L.T("menu.preset.delete", "Удалить пресет"));
    readonly List<ToolStripMenuItem> _layerItems = new();
    readonly Dictionary<string, ToolStripMenuItem> _driverItems = new();
    readonly ToolStripMenuItem _autoDriverItem = new();

    /// <summary>Не null — окно закрыто, чтобы открыться заново с новой темой, языком или файлами ресурсов.</summary>
    public ReopenState? Reopen { get; private set; }

    public MainForm(AppSettings settings, bool startHidden, EventWaitHandle? showEvent, string? snapshotPath = null,
        ControlId? select = null, ReopenState? reopen = null)
    {
        _settings = settings;
        _showEvent = showEvent;
        _snapshotPath = snapshotPath;
        _interactive = snapshotPath == null;
        _allowVisible = !startHidden;

        Text = AppName;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Window;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(1180), workArea.Width - 40), Math.Min(Theme.S(760), workArea.Height - 60));
        MinimumSize = new Size(Math.Min(Theme.S(1060), workArea.Width), Math.Min(Theme.S(700), workArea.Height));
        if (reopen != null)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = reopen.Bounds;
            if (reopen.State == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            _layer = reopen.Layer;
            select ??= reopen.Selected;
        }

        var menu = BuildMenu();
        Controls.Add(BuildBody());
        Controls.Add(_notices);
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());
        Controls.Add(menu);
        MainMenuStrip = menu;
        BuildTray();
        Theme.ApplyTo(this);

        _pad.ActionProvider = id => CurrentLayer.Get(id);
        _pad.NeedsAgent = _settings.NeedsAgent;
        _pad.SelectionChanged += (_, id) => SelectControl(id);
        _editor.ActionChanged += (_, _) => OnActionEdited();
        _editor.TestRequested += (_, _) =>
        {
            if (_editor.Action is { } action) _ = RunActionAsync(action, fromTest: true);
        };
        _deviceTimer.Tick += (_, _) => RefreshDevice();
        _saveTimer.Tick += (_, _) => SaveNow();

        if (_interactive)
        {
            _agent = new HotkeyAgent();
            _agent.Triggered += OnTrigger;
            Autostart.RefreshPathIfEnabled();
        }

        _settings.EnsureTriggers();
        RefreshDevice();
        RefreshPresets();
        RebuildLayerUi();
        UpdateDriverMenu();
        SelectControl(select ?? ControlId.Key1);
        UpdateDirty();
        ApplyAgent();
        SetStatus(L.T("status.ready", "Выберите клавишу или действие ручки слева, настройте его справа и нажмите «Записать в устройство»."));
    }

    Layer CurrentLayer => _settings.Profile.Layers[_layer];

    // ───────────────────────── Построение интерфейса ─────────────────────────

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Theme.Surface, Padding = new Padding(Theme.S(6), Theme.S(2), 0, Theme.S(2)) };

        var file = new ToolStripMenuItem(L.T("menu.file", "Файл"));
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.new", "Новый пресет…"), null, (_, _) => NewPreset()) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.saveAs", "Сохранить как новый пресет…"), null, (_, _) => DuplicatePreset())
            { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.rename", "Переименовать пресет…"), null, (_, _) => RenamePreset()) { ShortcutKeys = Keys.F2 });
        _deletePresetItem.Click += (_, _) => DeletePreset();
        file.DropDownItems.Add(_deletePresetItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.import", "Импорт пресета…"), null, (_, _) => ImportPreset()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.export", "Экспорт пресета…"), null, (_, _) => ExportPreset()) { ShortcutKeys = Keys.Control | Keys.E });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.folder", "Открыть папку пресетов"), null, (_, _) => OpenFolder(AppPaths.Presets)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.clearLayer", "Очистить текущий слой"), null, (_, _) => ClearLayer()));
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.reset", "Вернуть пример настроек"), null, (_, _) => ResetToDefault()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.exit", "Выход"), null, (_, _) => ExitApp()));
        file.DropDownOpening += (_, _) => _deletePresetItem.Enabled = PresetStore.Names().Count > 1;

        var device = new ToolStripMenuItem(L.T("menu.device", "Устройство"));
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.write", "Записать в устройство"), null, async (_, _) => await WriteToDeviceAsync()) { ShortcutKeys = Keys.F5 });
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.monitor", "Монитор нажатий…"), null, (_, _) => ShowMonitor()) { ShortcutKeys = Keys.F8 });
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.wizard", "Проверить запись…"), null, (_, _) => ShowWizard()));
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.refresh", "Обновить состояние"), null, (_, _) => RefreshDevice()));
        device.DropDownItems.Add(new ToolStripSeparator());

        var driver = new ToolStripMenuItem(L.T("menu.driver", "Драйвер"));
        _autoDriverItem.Click += (_, _) => SetDriver(null);
        driver.DropDownItems.Add(_autoDriverItem);
        driver.DropDownItems.Add(new ToolStripSeparator());
        foreach (var def in DriverStore.All)
        {
            string id = def.Id;
            var item = new ToolStripMenuItem(def.Title, null, (_, _) => SetDriver(id)) { ToolTipText = $"USB {def.Vid:X4}:{def.Pid:X4}" };
            _driverItems[id] = item;
            driver.DropDownItems.Add(item);
        }
        driver.DropDownItems.Add(new ToolStripSeparator());
        driver.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.driver.folder", "Открыть папку драйверов"), null, (_, _) => OpenFolder(AppPaths.Drivers)));
        device.DropDownItems.Add(driver);

        for (int n = 1; n <= Profile.MaxLayers; n++)
        {
            int count = n;
            var item = new ToolStripMenuItem(n.ToString(), null, (_, _) => SetLayerCount(count));
            _layerItems.Add(item);
            _layersMenu.DropDownItems.Add(item);
        }
        device.DropDownItems.Add(_layersMenu);

        var options = new ToolStripMenuItem(L.T("menu.settings", "Настройки"));
        options.DropDownItems.Add(BuildThemeMenu());
        options.DropDownItems.Add(BuildLanguageMenu());
        options.DropDownItems.Add(new ToolStripSeparator());
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _trayItem.Click += (_, _) =>
        {
            _settings.MinimizeToTray = !_settings.MinimizeToTray;
            _trayItem.Checked = _settings.MinimizeToTray;
            ScheduleSave();
        };
        options.DropDownItems.Add(_autostartItem);
        options.DropDownItems.Add(_trayItem);
        options.DropDownItems.Add(new ToolStripSeparator());
        options.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.dataFolder", "Открыть папку с данными (user_data)"), null, (_, _) => OpenFolder(AppPaths.UserData)));
        options.DropDownOpening += (_, _) =>
        {
            _autostartItem.Checked = Autostart.IsEnabled;
            _trayItem.Checked = _settings.MinimizeToTray;
        };

        var help = new ToolStripMenuItem(L.T("menu.help", "Справка"));
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.howItWorks", "Как это работает"), null, (_, _) => ShowHowItWorks()));
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.checkFiles", "Проверить файлы программы"), null, (_, _) => RequestReopen()));
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.checkUpdates", "Проверить обновления"), null, (_, _) => NoticeBar.OpenReleases()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.about", "О программе"), null, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, device, options, help });
        return menu;
    }

    ToolStripMenuItem BuildThemeMenu()
    {
        var menu = new ToolStripMenuItem(L.T("menu.theme", "Тема"));
        if (menu.DropDown is ToolStripDropDownMenu dropDown) dropDown.ShowCheckMargin = true;
        if (ThemeStore.All.Count == 0)
            menu.DropDownItems.Add(new ToolStripMenuItem(ThemeStore.Current.Title) { Checked = true, Enabled = false });
        foreach (var theme in ThemeStore.All)
        {
            string id = theme.Id;
            menu.DropDownItems.Add(new ToolStripMenuItem(theme.Title, ThemeStore.Swatch(theme, Theme.S(16)), (_, _) => SetTheme(id))
            {
                Checked = id == ThemeStore.Current.Id,
            });
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.theme.folder", "Открыть папку тем"), null, (_, _) => OpenFolder(AppPaths.Themes)));
        return menu;
    }

    ToolStripMenuItem BuildLanguageMenu()
    {
        var menu = new ToolStripMenuItem(L.T("menu.language", "Язык / Language"));
        if (L.Available.Count == 0)
            menu.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.language.builtin", "Русский (встроенный)")) { Checked = true, Enabled = false });
        foreach (var language in L.Available)
        {
            string code = language.Code;
            menu.DropDownItems.Add(new ToolStripMenuItem(language.Name, null, (_, _) => SetLanguage(code)) { Checked = code == L.Code });
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.language.folder", "Открыть папку языков"), null, (_, _) => OpenFolder(AppPaths.Languages)));
        return menu;
    }

    Control BuildHeader()
    {
        // Фон цвета рамки + отступ в 1 пиксель снизу = тонкая линия под шапкой.
        var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(66), BackColor = Theme.Border, Padding = new Padding(0, 0, 0, 1) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S(18), 0, Theme.S(18), 0),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _deviceDot.Size = new Size(Theme.S(12), Theme.S(12));
        _deviceDot.Anchor = AnchorStyles.Left;
        _deviceDot.Margin = new Padding(0, 0, Theme.S(8), 0);

        _deviceText.AutoSize = true;
        _deviceText.Anchor = AnchorStyles.Left;
        _deviceText.Font = Theme.Strong;
        _deviceText.ForeColor = Theme.Text;
        _deviceText.Margin = new Padding(0);

        _dirtyLabel.AutoSize = true;
        _dirtyLabel.Anchor = AnchorStyles.Right;
        _dirtyLabel.ForeColor = Theme.Warning;
        _dirtyLabel.Margin = new Padding(Theme.S(12), 0, Theme.S(16), 0);

        _presetLabel.Text = L.T("header.preset", "Пресет:");
        _presetLabel.AutoSize = true;
        _presetLabel.Anchor = AnchorStyles.Right;
        _presetLabel.ForeColor = Theme.Muted;
        _presetLabel.Margin = new Padding(0, 0, Theme.S(6), 0);

        _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetBox.Width = Theme.S(180);
        _presetBox.MaxDropDownItems = 16;
        _presetBox.Anchor = AnchorStyles.Left;
        _presetBox.Margin = new Padding(0, 0, Theme.S(16), 0);
        _presetBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressPresetEvent || _presetBox.SelectedItem is not string name) return;
            SwitchPreset(name);
        };

        _layerLabel.Text = L.T("header.layer", "Слой:");
        _layerLabel.AutoSize = true;
        _layerLabel.Anchor = AnchorStyles.Right;
        _layerLabel.ForeColor = Theme.Muted;
        _layerLabel.Margin = new Padding(0, 0, Theme.S(6), 0);

        _layerBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _layerBox.Width = Theme.S(100);
        _layerBox.Anchor = AnchorStyles.Left;
        _layerBox.Margin = new Padding(0, 0, Theme.S(16), 0);
        _layerBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressLayerEvent || _layerBox.SelectedIndex < 0) return;
            _layer = _layerBox.SelectedIndex;
            SelectControl(_pad.Selected);
            _pad.Invalidate();
        };

        _writeButton.Text = L.T("header.write", "Записать в устройство");
        _writeButton.AutoSize = true;
        _writeButton.MinimumSize = new Size(Theme.S(200), Theme.S(40));
        _writeButton.Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0);
        _writeButton.FlatStyle = FlatStyle.Flat;
        _writeButton.BackColor = Theme.Accent;
        _writeButton.ForeColor = Theme.OnAccent;
        _writeButton.Font = Theme.Strong;
        _writeButton.Cursor = Cursors.Hand;
        _writeButton.Anchor = AnchorStyles.Right;
        _writeButton.Margin = new Padding(0);
        _writeButton.FlatAppearance.BorderSize = 0;
        _writeButton.FlatAppearance.MouseOverBackColor = Theme.AccentDark;
        _writeButton.FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
        _writeButton.Click += async (_, _) => await WriteToDeviceAsync();

        grid.Controls.Add(_deviceDot, 0, 0);
        grid.Controls.Add(_deviceText, 1, 0);
        grid.Controls.Add(new Panel { Margin = new Padding(0), Size = new Size(1, 1) }, 2, 0);
        grid.Controls.Add(_dirtyLabel, 3, 0);
        grid.Controls.Add(_presetLabel, 4, 0);
        grid.Controls.Add(_presetBox, 5, 0);
        grid.Controls.Add(_layerLabel, 6, 0);
        grid.Controls.Add(_layerBox, 7, 0);
        grid.Controls.Add(_writeButton, 8, 0);
        header.Controls.Add(grid);
        return header;
    }

    Control BuildBody()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Window,
            Padding = new Padding(Theme.S(16)),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(470)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = Card(_pad);
        left.Margin = new Padding(0, 0, Theme.S(16), 0);
        var right = Card(_editor);
        right.Margin = new Padding(0);
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);
        return grid;
    }

    static Panel Card(Control content)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        return card;
    }

    StatusStrip BuildStatusBar()
    {
        var strip = new StatusStrip
        {
            SizingGrip = false,
            BackColor = Theme.Surface,
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0),
        };
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Theme.Text;
        _driverStatus.ForeColor = Theme.Muted;
        _driverStatus.Margin = new Padding(Theme.S(12), 0, Theme.S(12), 0);
        _agentStatus.ForeColor = Theme.Muted;
        strip.Items.Add(_status);
        strip.Items.Add(_driverStatus);
        strip.Items.Add(_agentStatus);
        return strip;
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem(L.F("tray.open", "Открыть {0}", AppName), null, (_, _) => ShowFromTray());
        open.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.write", "Записать в устройство"), null, async (_, _) => await WriteToDeviceAsync()));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.monitor", "Монитор нажатий…"), null, (_, _) => ShowMonitor()));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.wizard", "Проверить запись…"), null, (_, _) => ShowWizard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.exit", "Выход"), null, (_, _) => ExitApp()));

        _tray.ContextMenuStrip = menu;
        _tray.Icon = Theme.SmallIcon;
        _tray.Text = AppName;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
        _tray.Visible = _interactive;
    }

    // ───────────────────────── Окно и трей ─────────────────────────

    protected override void SetVisibleCore(bool value)
    {
        // При запуске с --tray окно не показываем, но создаём, чтобы работали горячие клавиши и WM_DEVICECHANGE.
        if (!_allowVisible)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_showEvent != null && _showWait == null)
        {
            // Повторный запуск программы просит уже работающую копию показать окно.
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
            {
                try { BeginInvoke(ShowFromTray); }
                catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, false);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_snapshotPath == null) return;

        // Режим --snapshot: сохранить картинку окна и выйти (для проверки вёрстки).
        await Task.Delay(500);
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(_snapshotPath, ImageFormat.Png);
        }
        _exiting = true;
        Close();
    }

    void ShowFromTray()
    {
        _allowVisible = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    void ShowMonitor()
    {
        if (_monitor is { IsDisposed: false })
        {
            _monitor.Activate();
            return;
        }
        _monitor = new MonitorForm();
        _monitor.FormClosed += (_, _) => _monitor = null;
        if (Visible) _monitor.Show(this);
        else _monitor.Show();
    }

    void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Закрывает окно, чтобы программа открыла его заново: так применяются тема, язык и перечитываются файлы ресурсов.</summary>
    void RequestReopen()
    {
        if (!_interactive) return;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Reopen = new ReopenState(bounds, WindowState, _pad.Selected, _layer);
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && _interactive && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _settings.TrayHintShown = true;
                ScheduleSave();
                _tray.ShowBalloonTip(5000, L.F("tray.background.title", "{0} работает в фоне", AppName),
                    L.T("tray.background.text", "Действия клавиш, которые выполняет программа, продолжают работать. Чтобы выйти, щёлкните правой кнопкой по значку."),
                    ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveNow();
        _monitor?.Close();
        _showWait?.Unregister(null);
        _agent?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _deviceTimer.Dispose();
        _saveTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Устройство подключили или отключили — проверяем с небольшой задержкой, пока Windows его настроит.
        if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            _deviceTimer.Stop();
            _deviceTimer.Start();
        }
        base.WndProc(ref m);
    }

    // ───────────────────────── Логика ─────────────────────────

    void SelectControl(ControlId id)
    {
        _pad.Selected = id;
        var action = CurrentLayer.Get(id);
        _editor.LoadAction(id, action);
        _editor.SetExecution(_settings.NeedsAgent(action));
    }

    void OnActionEdited()
    {
        _settings.EnsureTriggers();
        if (_editor.Action is { } action) _editor.SetExecution(_settings.NeedsAgent(action));
        _pad.Invalidate();
        UpdateDirty();
        ApplyAgent();
        ScheduleSave();
    }

    void RefreshDevice()
    {
        _deviceTimer.Stop();
        _device = MacroPadDevice.FindConfigInterface();
        if (_device != null && MacroPadDevice.DetectDriver() is { } detected && detected.Id != _settings.DetectedDriverId)
        {
            _settings.DetectedDriverId = detected.Id;
            AfterDriverChanged();
        }

        if (_device != null)
        {
            _deviceDot.DotColor = Theme.Success;
            _deviceText.Text = L.F("device.connected", "Устройство подключено  ·  {0:X4}:{1:X4}", _device.VendorId, _device.ProductId);
        }
        else
        {
            _deviceDot.DotColor = Theme.Danger;
            _deviceText.Text = L.T("device.notFound", "Устройство не найдено — подключите его USB-кабелем");
        }
        _driverStatus.Text = L.F("status.driver", "Драйвер: {0}", _settings.EffectiveDriver.Title);
    }

    async void ShowWizard()
    {
        _monitor?.Close();
        using var wizard = new ProtocolWizardForm();
        if (Visible) wizard.ShowDialog(this);
        else wizard.ShowDialog();

        // В клавиатуре теперь тестовые буквы — настройки придётся записать заново.
        _settings.LastWrittenSignature = null;
        var driver = wizard.Result is ProtocolMode mode
            ? _settings.EffectiveDriver.Protocol == mode ? _settings.EffectiveDriver : DriverStore.FirstWith(mode)
            : null;
        if (driver == null)
        {
            UpdateDirty();
            ScheduleSave();
            return;
        }

        _settings.DriverId = driver.Id;
        if (wizard.KnobSlots != null)
        {
            _settings.LearnedKnobSlots ??= new();
            _settings.LearnedKnobSlots[driver.Id] = wizard.KnobSlots;
        }
        AfterDriverChanged();
        RefreshDevice();
        if (MessageBox.Show(this, L.F("wizard.apply", "Драйвер «{0}» сохранён.\n\nЗаписать в клавиатуру ваши настройки?", driver.Title),
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            await WriteToDeviceAsync();
    }

    void SetDriver(string? id)
    {
        if (_settings.DriverId == id) return;
        _settings.DriverId = id;
        AfterDriverChanged();
        RefreshDevice();
        SetStatus(L.F("status.driverChanged", "Драйвер: {0}. Нажмите «Записать в устройство», чтобы применить.", _settings.EffectiveDriver.Title));
    }

    void AfterDriverChanged()
    {
        _settings.EnsureTriggers();
        RebuildLayerUi();
        UpdateDriverMenu();
        SelectControl(_pad.Selected);
        _pad.Invalidate();
        UpdateDirty();
        ApplyAgent();
        ScheduleSave();
    }

    void UpdateDriverMenu()
    {
        string? detected = DriverStore.Find(_settings.DetectedDriverId)?.Title;
        _autoDriverItem.Text = _settings.DriverId == null && detected != null
            ? L.F("menu.driver.autoNow", "Определять автоматически (сейчас: {0})", detected)
            : L.T("menu.driver.auto", "Определять автоматически");
        _autoDriverItem.Checked = _settings.DriverId == null;
        foreach (var (id, item) in _driverItems) item.Checked = id == _settings.DriverId;
        _layersMenu.Enabled = _settings.EffectiveProtocol == ProtocolMode.Legacy;
        _layersMenu.ToolTipText = _layersMenu.Enabled ? "" : L.T("menu.layers.unsupported", "Этот драйвер не поддерживает слои");
    }

    void UpdateDirty()
    {
        _settings.EnsureTriggers();
        string signature = ProfileCompiler.Signature(ProfileCompiler.Compile(_settings));
        _dirtyLabel.Text = _settings.LastWrittenSignature == null
            ? L.T("header.notWritten", "● Ещё не записано в устройство")
            : L.T("header.unsaved", "● Есть незаписанные изменения");
        _dirtyLabel.Visible = signature != _settings.LastWrittenSignature;
    }

    async Task WriteToDeviceAsync()
    {
        if (_writing) return;
        RefreshDevice();
        if (_device is not { } device)
        {
            MessageBox.Show(this,
                L.T("write.noDevice", "Макро-клавиатура не найдена.\n\nПодключите её USB-кабелем (по Bluetooth запись не работает) и попробуйте ещё раз."),
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.EnsureTriggers();
        var packets = ProfileCompiler.Compile(_settings);
        bool sendInit = ProfileCompiler.SendsInitPacket(_settings.EffectiveProtocol);
        _writing = true;
        _writeButton.Enabled = false;
        UseWaitCursor = true;
        SetStatus(L.T("status.writing", "Запись в устройство…"));
        try
        {
            var progress = new Progress<int>(p => SetStatus(L.F("status.writingProgress", "Запись в устройство… {0}%", p)));
            await Task.Run(() =>
            {
                using var pad = MacroPadDevice.Open(device);
                pad.Write(packets, sendInit, progress);
            });
            _settings.LastWrittenSignature = ProfileCompiler.Signature(packets);
            SaveNow();
            SetStatus(L.T("status.written", "✓ Записано. Переподключите кабель клавиатуры и проверьте в «Устройство → Монитор нажатий» (F8)."));
        }
        catch (Exception ex)
        {
            SetStatus(L.F("status.writeError", "Ошибка записи: {0}", ex.Message));
            MessageBox.Show(this, L.F("write.error", "Не удалось записать настройки в устройство:\n\n{0}\n\nПереподключите клавиатуру и попробуйте ещё раз.", ex.Message),
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _writing = false;
            _writeButton.Enabled = true;
            UseWaitCursor = false;
            UpdateDirty();
            ApplyAgent();
        }
    }

    /// <summary>Действия, которые выполняет фоновый агент (у них есть служебная клавиша).</summary>
    IEnumerable<(ControlId Control, PadAction Action)> AgentActions() =>
        _settings.Profile.All(_settings.EffectiveLayerCount)
            .Where(x => x.Action.TriggerSlot != null)
            .Select(x => (x.Control, x.Action));

    void ApplyAgent()
    {
        if (_agent == null) return;
        var slots = AgentActions().Select(x => x.Action.TriggerSlot).OfType<int>().ToHashSet();
        if (!slots.SetEquals(_registeredSlots) || _failedSlots.Count > 0)
        {
            _failedSlots = _agent.Apply(slots);
            _registeredSlots = slots;
        }

        if (_failedSlots.Count > 0)
        {
            _agentStatus.Text = L.F("status.agentBusy", "⚠ Служебные клавиши заняты другой программой: {0}", string.Join(", ", _failedSlots.Select(TriggerKeys.Describe)));
            _agentStatus.ForeColor = Theme.Danger;
        }
        else
        {
            _agentStatus.Text = slots.Count == 0
                ? L.T("status.agentNone", "Фоновые действия: не используются")
                : L.F("status.agentCount", "Фоновые действия: {0}, работают", slots.Count);
            _agentStatus.ForeColor = Theme.Muted;
        }
    }

    void OnTrigger(int slot)
    {
        var (control, action) = AgentActions().FirstOrDefault(x => x.Action.TriggerSlot == slot);
        if (action == null) return;
        _monitor?.LogAgent($"{TriggerKeys.Describe(slot)} → {UiText.Title(control)}: {BindingText.Summary(action)}");
        _ = RunActionAsync(action, fromTest: false);
    }

    async Task RunActionAsync(PadAction action, bool fromTest)
    {
        try
        {
            await ActionRunner.ExecuteAsync(action, this);
        }
        catch (Exception ex)
        {
            _monitor?.LogAgent(L.F("monitor.agentError", "ошибка: {0}", ex.Message));
            string title = L.T("action.failed", "Не удалось выполнить действие");
            if (fromTest || Visible)
                MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                _tray.ShowBalloonTip(5000, title, ex.Message, ToolTipIcon.Warning);
        }
    }

    void RebuildLayerUi()
    {
        _settings.LayerCount = Math.Clamp(_settings.LayerCount, 1, Profile.MaxLayers);
        int layers = _settings.EffectiveLayerCount;
        if (_layer >= layers) _layer = 0;

        _suppressLayerEvent = true;
        _layerBox.BeginUpdate();
        _layerBox.Items.Clear();
        for (int i = 0; i < layers; i++) _layerBox.Items.Add(L.F("header.layerN", "Слой {0}", i + 1));
        _layerBox.EndUpdate();
        _layerBox.SelectedIndex = _layer;
        _suppressLayerEvent = false;

        _layerBox.Visible = _layerLabel.Visible = layers > 1;
        for (int i = 0; i < _layerItems.Count; i++) _layerItems[i].Checked = i + 1 == _settings.LayerCount;
    }

    void SetLayerCount(int count)
    {
        if (count == _settings.LayerCount) return;
        _settings.LayerCount = count;
        RebuildLayerUi();
        SelectControl(_pad.Selected);
        _pad.Invalidate();
        UpdateDirty();
        ApplyAgent();
        ScheduleSave();
        if (count > 1)
            SetStatus(L.T("status.layers", "Слои переключаются на самом устройстве (обычно отдельной кнопкой или комбинацией) — это зависит от модели."));
    }

    void AfterProfileReplaced()
    {
        _settings.Profile.Normalize();
        _settings.EnsureTriggers();
        SelectControl(_pad.Selected);
        _pad.Invalidate();
        UpdateDirty();
        ApplyAgent();
        ScheduleSave();
    }

    void ToggleAutostart()
    {
        try
        {
            Autostart.Set(!Autostart.IsEnabled);
            SetStatus(Autostart.IsEnabled
                ? L.F("status.autostartOn", "Автозапуск включён: {0} будет запускаться в трее вместе с Windows.", AppName)
                : L.T("status.autostartOff", "Автозапуск выключен."));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("autostart.error", "Не удалось изменить автозапуск:\n\n{0}", ex.Message), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _autostartItem.Checked = Autostart.IsEnabled;
    }

    // ───────────────────────── Тема и язык ─────────────────────────

    void SetTheme(string id)
    {
        if (id == ThemeStore.Current.Id) return;
        _settings.Theme = id;
        RequestReopen();
    }

    void SetLanguage(string code)
    {
        if (code == L.Code) return;
        _settings.Language = code;
        RequestReopen();
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
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ───────────────────────── Пресеты ─────────────────────────

    void RefreshPresets()
    {
        _suppressPresetEvent = true;
        _presetBox.BeginUpdate();
        _presetBox.Items.Clear();
        foreach (var name in PresetStore.Names()) _presetBox.Items.Add(name);
        if (_settings.ActivePreset != null && !_presetBox.Items.Contains(_settings.ActivePreset)) _presetBox.Items.Add(_settings.ActivePreset);
        _presetBox.EndUpdate();
        _presetBox.SelectedItem = _settings.ActivePreset;
        _suppressPresetEvent = false;
    }

    /// <summary>Делает пресет активным и показывает его. Текущий перед этим сохраняется.</summary>
    void ActivatePreset(string name, Profile profile)
    {
        _settings.Profile = profile;
        _settings.ActivePreset = name;
        AfterProfileReplaced();
        SaveNow();
        RefreshPresets();
    }

    void SwitchPreset(string name)
    {
        if (name == _settings.ActivePreset) return;
        SaveNow();
        try
        {
            ActivatePreset(name, PresetStore.Load(name));
            SetStatus(L.F("status.presetSwitched", "Пресет «{0}». Нажмите «Записать в устройство», чтобы применить его.", name));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, L.F("preset.loadError", "Не удалось открыть пресет «{0}»:\n\n{1}", name, ex.Message), AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshPresets();
        }
    }

    /// <summary>Спрашивает название пресета. Null — отмена.</summary>
    string? AskPresetName(string title, string prompt, string initial, string? current = null) =>
        InputDialog.Ask(this, title, prompt, initial, text =>
        {
            string name = PresetStore.Clean(text);
            if (name.Length == 0) return L.T("preset.nameEmpty", "Введите название.");
            bool same = current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            return PresetStore.Exists(name) && !same ? L.F("preset.exists", "Пресет «{0}» уже есть — выберите другое название.", name) : null;
        }) is string answer ? PresetStore.Clean(answer) : null;

    void NewPreset()
    {
        string? name = AskPresetName(L.T("preset.new.title", "Новый пресет"),
            L.T("preset.new.prompt", "Название нового пресета. Он начнётся с примера настроек."),
            PresetStore.Unique(L.T("preset.new.name", "Новый пресет")));
        if (name == null) return;
        SaveNow();
        RunPresetCommand(() =>
        {
            var profile = Profile.CreateDefault();
            PresetStore.Save(name, profile);
            ActivatePreset(name, profile);
        });
    }

    void DuplicatePreset()
    {
        string? name = AskPresetName(L.T("preset.saveAs.title", "Сохранить как новый пресет"),
            L.T("preset.saveAs.prompt", "Название копии текущего пресета:"),
            PresetStore.Unique(_settings.ActivePreset ?? L.T("preset.default", "Основной")));
        if (name == null) return;
        SaveNow();
        RunPresetCommand(() =>
        {
            var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(_settings.Profile, SettingsStore.Json), SettingsStore.Json)!;
            PresetStore.Save(name, copy);
            ActivatePreset(name, copy);
        });
    }

    void RenamePreset()
    {
        string? current = _settings.ActivePreset;
        if (current == null) return;
        string? name = AskPresetName(L.T("preset.rename.title", "Переименовать пресет"),
            L.T("preset.rename.prompt", "Новое название пресета:"), current, current);
        if (name == null || name == current) return;
        SaveNow();
        RunPresetCommand(() =>
        {
            PresetStore.Rename(current, name);
            ActivatePreset(name, _settings.Profile);
        });
    }

    void DeletePreset()
    {
        string? current = _settings.ActivePreset;
        if (current == null || PresetStore.Names().Count <= 1) return;
        if (MessageBox.Show(this, L.F("preset.delete.confirm", "Удалить пресет «{0}»? Это нельзя отменить.", current), AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        RunPresetCommand(() =>
        {
            PresetStore.Delete(current);
            string next = PresetStore.Names()[0];
            ActivatePreset(next, PresetStore.Load(next));
        });
    }

    void ImportPreset()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L.T("preset.import.title", "Импорт пресета"),
            Filter = L.T("filter.preset", "Пресет OshiMakiKey (*.json)|*.json") + "|" + L.T("filter.all", "Все файлы (*.*)|*.*"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SaveNow();
        RunPresetCommand(() =>
        {
            var profile = SettingsStore.ReadProfile(dialog.FileName);
            string baseName = profile.Name.Length > 0 ? profile.Name : Path.GetFileNameWithoutExtension(dialog.FileName);
            string name = PresetStore.Unique(baseName.Length > 0 ? baseName : L.T("preset.new.name", "Новый пресет"));
            PresetStore.Save(name, profile);
            ActivatePreset(name, profile);
            SetStatus(L.F("status.presetImported", "Пресет «{0}» добавлен. Нажмите «Записать в устройство», чтобы применить его.", name));
        });
    }

    void ExportPreset()
    {
        using var dialog = new SaveFileDialog
        {
            Title = L.T("preset.export.title", "Экспорт пресета"),
            Filter = L.T("filter.preset", "Пресет OshiMakiKey (*.json)|*.json"),
            FileName = (_settings.ActivePreset ?? "preset") + ".json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunPresetCommand(() =>
        {
            SettingsStore.WriteProfile(_settings.Profile, dialog.FileName);
            SetStatus(L.F("status.presetExported", "Пресет сохранён: {0}", dialog.FileName));
        });
    }

    void RunPresetCommand(Action command)
    {
        try
        {
            command();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, L.F("preset.error", "Не удалось выполнить действие с пресетом:\n\n{0}", ex.Message), AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshPresets();
        }
    }

    void ClearLayer()
    {
        if (MessageBox.Show(this, L.T("clear.confirm", "Сбросить все действия текущего слоя на «Нет действия»?"), AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var id in Enum.GetValues<ControlId>()) CurrentLayer.Actions[id] = new PadAction();
        AfterProfileReplaced();
    }

    void ResetToDefault()
    {
        if (MessageBox.Show(this, L.T("reset.confirm", "Заменить все настройки текущего пресета примером (копировать, вставить, отменить, музыка, громкость)?"), AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _settings.Profile = Profile.CreateDefault();
        AfterProfileReplaced();
    }

    // ───────────────────────── Справка ─────────────────────────

    void ShowHowItWorks() => MessageBox.Show(this,
        L.F("help.text",
            "1. Слева выберите клавишу или действие ручки.\n2. Справа выберите, что оно должно делать.\n3. Нажмите «Записать в устройство».\n4. Проверьте результат в «Устройство → Монитор нажатий» (F8).\n\nДействия с отметкой ✓ записываются в память самой клавиатуры и работают даже без программы.\n\nДействия с отметкой ⚙ клавиатура сама выполнить не может (у новой ревизии CH552 это мультимедиа, мышь, последовательности нажатий и все системные действия). Для них устройство отправляет служебную клавишу F13–F24, а {0}, работающая в трее, выполняет действие. Включите «Настройки → Запускать вместе с Windows», чтобы это работало всегда.\n\nРаскладки хранятся в пресетах — переключайте их в шапке окна. Запись идёт только по USB-кабелю. Драйвер Windows не нужен — используется стандартный.",
            AppName),
        L.T("menu.howItWorks", "Как это работает"), MessageBoxButtons.OK, MessageBoxIcon.Information);

    void ShowAbout() => MessageBox.Show(this,
        L.F("about.text",
            "{0} {1}\n\nНастройка макро-клавиатур на чипах CH552 / CH57x: 6 клавиш и ручка.\n\nДрайвер: {2}\nТема: {3}\nЯзык: {4}\n\nПротоколы: kriomant/ch57x-keyboard-tool и barkleesanders/padclaude.\n\nДанные программы: {5}\n{6}",
            AppName, Branding.Version, _settings.EffectiveDriver.Title, ThemeStore.Current.Title,
            L.Available.FirstOrDefault(l => l.Code == L.Code)?.Name ?? L.T("menu.language.builtin", "Русский (встроенный)"),
            AppPaths.UserData, Branding.Website),
        L.T("menu.about", "О программе"), MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ───────────────────────── Служебное ─────────────────────────

    void SetStatus(string text) => _status.Text = text;

    void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveNow()
    {
        _saveTimer.Stop();
        if (!_interactive) return;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            SetStatus(L.F("status.saveError", "Не удалось сохранить настройки: {0}", ex.Message));
        }
    }
}
