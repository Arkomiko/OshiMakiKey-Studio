using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Главное и единственное окно программы (кроме монитора нажатий): схема устройства, редактор действия, пресеты,
/// запись в устройство, значок в трее и фоновый агент. Настройки, мастер первого запуска, проверка записи и
/// «О программе» открываются страницами на месте основного содержимого (<see cref="ShowPage"/>), а смена темы
/// или языка перестраивает содержимое этого же окна (<see cref="BuildUi"/>) — окно не закрывается.
/// </summary>
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
    string _savedSnapshot = "";
    HidDeviceInfo? _device;
    HashSet<int> _registeredSlots = new();
    List<int> _failedSlots = new();
    RegisteredWaitHandle? _showWait;
    MonitorForm? _monitor;
    TourOverlay? _tour;

    // Элементы интерфейса создаются заново в BuildUi при каждой смене темы или языка, поэтому они не readonly.
    DevicePanel _pad = null!;
    BindingEditor _editor = null!;
    NoticeBar _notices = null!;
    StatusDot _deviceDot = null!;
    Label _deviceText = null!;
    Label _writeState = null!;
    Panel _deviceColumn = null!;
    Label _presetLabel = null!;
    ThemedComboBox _presetBox = null!;
    Button _savePreset = null!;
    Button _presetMore = null!;
    ContextMenuStrip _presetMenu = null!;
    Label _layerLabel = null!;
    ThemedComboBox _layerBox = null!;
    Button _writeButton = null!;
    MenuStrip _menu = null!;
    StatusStrip _statusStrip = null!;
    ToolStripStatusLabel _status = null!;
    ToolStripStatusLabel _driverStatus = null!;
    ToolStripStatusLabel _agentStatus = null!;
    ToolStripMenuItem _layersMenu = null!;
    ToolStripMenuItem _deletePresetItem = null!;
    ToolStripMenuItem _wizardItem = null!;
    ToolStripMenuItem _autoDriverItem = null!;
    readonly List<ToolStripMenuItem> _layerItems = new();
    readonly Dictionary<string, ToolStripMenuItem> _driverItems = new();
    readonly ToolTip _tips = new();
    readonly NotifyIcon _tray = new();
    readonly HotkeyAgent? _agent;
    readonly HoldFilter _holdFilter = new();
    readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 700 };

    /// <summary>Открытые страницы; верхняя показана вместо основного содержимого (проверка записи — поверх мастера).</summary>
    readonly Stack<Control> _pages = new();
    /// <summary>Основные элементы, скрытые на время показа страниц.</summary>
    List<Control> _hiddenMain = new();

    /// <summary>Для --snapshot --tour N: снимок окна с открытым шагом тура.</summary>
    public int SnapshotTourStep { get; set; } = -1;

    /// <summary>Для --snapshot --switch-theme/--switch-lang: сменить тему или язык уже открытого окна перед снимком.</summary>
    public (string? Theme, string? Language) SnapshotSwitch { get; set; }

    /// <summary>Для --snapshot --page settings|about|setup: снимок окна с открытой страницей.</summary>
    public string? SnapshotPage { get; set; }

    /// <summary>Для --snapshot --page settings --preview-theme id: выбрать тему в открытых настройках (проверка мгновенного применения).</summary>
    public string? SnapshotPreviewTheme { get; set; }

    /// <summary>
    /// Самопроверка перетаскивания (--drop-test): по очереди тащит клавишу 5 на каждое действие ручки и проверяет,
    /// что действие оказалось там, куда его бросили. Возвращает строки PASS/FAIL.
    /// </summary>
    public List<string> RunDropTest()
    {
        var report = new List<string>();
        _settings.ConfirmOverwrite = false;
        var targets = _settings.Controls.Where(c => !Model.Controls.IsKey(c)).Append(ControlId.Key1).ToList();
        foreach (var (inList, copy) in new[] { (false, false), (true, false), (false, true) })
        {
            foreach (var target in targets)
            {
                _settings.Profile = Profile.CreateDefault();
                AfterProfileReplaced();
                string expected = BindingText.Summary(CurrentLayer.Get(ControlId.Key5));
                _pad.SimulateDrag(_pad.PointOf(ControlId.Key5, inList), _pad.PointOf(target, inList), copy);
                string actual = BindingText.Summary(CurrentLayer.Get(target));
                bool sourceOk = copy ? CurrentLayer.Get(ControlId.Key5).Kind == ActionKind.Media : CurrentLayer.Get(ControlId.Key5).Kind == ActionKind.None;
                bool written = ProfileCompiler.Compile(_settings).Count > 0 && _settings.NeedsAgent(CurrentLayer.Get(target)) == true
                               && CurrentLayer.Get(target).TriggerSlot != null;
                bool ok = actual == expected && sourceOk && written;
                report.Add($"{(ok ? "PASS" : "FAIL")}  {(inList ? "список" : "рисунок")}{(copy ? ", копия" : "")}: Клавиша 5 → {UiText.Title(target)}: «{actual}», служебная клавиша {CurrentLayer.Get(target).TriggerSlot?.ToString() ?? "нет"}");
            }
        }
        return report;
    }

    public MainForm(AppSettings settings, bool startHidden, EventWaitHandle? showEvent, string? snapshotPath = null, ControlId? select = null)
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
        KeyPreview = true;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(1180), workArea.Width - 40), Math.Min(Theme.S(760), workArea.Height - 60));
        MinimumSize = new Size(Math.Min(Theme.S(1060), workArea.Width), Math.Min(Theme.S(700), workArea.Height));

        _tray.Icon = Theme.SmallIcon;
        _tray.Text = AppName;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
        _tray.Visible = _interactive;
        _deviceTimer.Tick += (_, _) => RefreshDevice();
        _saveTimer.Tick += (_, _) => SaveNow();

        if (_interactive)
        {
            _agent = new HotkeyAgent();
            _agent.Triggered += OnTrigger;
            Autostart.RefreshPathIfEnabled();
        }

        _settings.EnsureTriggers();
        _savedSnapshot = SavedSnapshotOf(_settings.ActivePreset);
        BuildUi(select);
    }

    Layer CurrentLayer => _settings.Profile.Layers[_layer];

    // ───────────────────────── Построение интерфейса ─────────────────────────

    /// <summary>
    /// Строит содержимое окна в текущих теме и языке. При повторном вызове старые элементы удаляются и создаются
    /// заново; пресет, слой и выбранная кнопка сохраняются, открытые страницы остаются на месте.
    /// </summary>
    void BuildUi(ControlId? select)
    {
        SuspendLayout();
        foreach (var control in Controls.Cast<Control>().Where(c => !_pages.Contains(c) && c != _tour && !Transition.IsCover(c)).ToList())
        {
            Controls.Remove(control);
            control.Dispose();
        }
        _presetMenu?.Dispose();
        _tips.RemoveAll();
        _layerItems.Clear();
        _driverItems.Clear();

        _pad = new DevicePanel();
        _editor = new BindingEditor();
        _notices = new NoticeBar();
        _deviceDot = new StatusDot();
        _deviceText = new Label();
        _writeState = new Label();
        _deviceColumn = new Panel();
        _presetLabel = new Label();
        _presetBox = new ThemedComboBox();
        _savePreset = new Button();
        _presetMore = new Button();
        _presetMenu = new ContextMenuStrip();
        _layerLabel = new Label();
        _layerBox = new ThemedComboBox();
        _writeButton = new Button();
        _menu = new MenuStrip();
        _statusStrip = new StatusStrip();
        _status = new ToolStripStatusLabel();
        _driverStatus = new ToolStripStatusLabel();
        _agentStatus = new ToolStripStatusLabel();
        _layersMenu = new ToolStripMenuItem(L.T("menu.layers", "Количество слоёв"));
        _deletePresetItem = new ToolStripMenuItem(L.T("menu.preset.delete", "Удалить пресет"));
        _wizardItem = new ToolStripMenuItem(L.T("menu.wizard", "Проверить запись…"));
        _autoDriverItem = new ToolStripMenuItem();

        BackColor = Theme.Window;
        _settings.EnsureTriggers();
        UiText.Driver = _settings.EffectiveDriver;
        _pad.Driver = _settings.EffectiveDriver;

        BuildMenu();
        Controls.Add(BuildBody());
        Controls.Add(_notices);
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        BuildTrayMenu();
        Theme.ApplyTo(this);

        _pad.ActionProvider = id => CurrentLayer.Get(id);
        _pad.NeedsAgent = _settings.NeedsAgent;
        _pad.SelectionChanged += (_, id) => SelectControl(id);
        _pad.ActionDropped += (_, drop) => OnActionDropped(drop);
        _editor.ActionChanged += (_, _) => OnActionEdited();
        _editor.TestRequested += (_, _) =>
        {
            if (_editor.Action is { } action) _ = RunActionAsync(action, fromTest: true);
        };
        if (_pages.Count > 0) HideMain();
        ResumeLayout(true);

        RefreshDevice();
        RefreshPresets();
        RebuildLayerUi();
        UpdateDriverMenu();
        SelectControl(select is ControlId id && _settings.Controls.Contains(id) ? id : _settings.Controls[0]);
        UpdateDirty();
        _registeredSlots = new(); // строка состояния новая — пусть агент заново покажет своё состояние
        ApplyAgent();
        SetStatus(L.T("status.ready", "Выберите клавишу или действие ручки слева, настройте его справа и нажмите «Записать в устройство»."));
        ThemeBackdrop.Update(this);
    }

    void BuildMenu()
    {
        _menu.BackColor = Theme.Surface;
        _menu.Padding = new Padding(Theme.S(6), Theme.S(2), 0, Theme.S(2));

        var file = new ToolStripMenuItem(L.T("menu.file", "Файл"));
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.new", "Новый пресет…"), null, (_, _) => NewPreset()) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.save", "Сохранить пресет"), null, (_, _) => QuickSave()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.saveAs", "Сохранить пресет как…"), null, (_, _) => SavePresetAs())
            { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.rename", "Переименовать пресет…"), null, (_, _) => RenamePreset()) { ShortcutKeys = Keys.F2 });
        _deletePresetItem.Click += (_, _) => DeletePreset();
        file.DropDownItems.Add(_deletePresetItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.import", "Импорт пресета…"), null, (_, _) => ImportPreset()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.export", "Экспорт пресета…"), null, (_, _) => ExportPreset()) { ShortcutKeys = Keys.Control | Keys.E });
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.preset.folder", "Открыть папку с пресетами"), null, (_, _) => OpenFolder(AppPaths.Presets)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.clearLayer", "Очистить текущий слой"), null, (_, _) => ClearLayer()));
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.reset", "Вернуть пример настроек"), null, (_, _) => ResetToDefault()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.exit", "Выход"), null, (_, _) => ExitApp()));
        file.DropDownOpening += (_, _) => _deletePresetItem.Enabled = PresetStore.Names().Count > 1;

        var device = new ToolStripMenuItem(L.T("menu.device", "Устройство"));
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.write", "Записать в устройство"), null, async (_, _) => await WriteToDeviceAsync()) { ShortcutKeys = Keys.F5 });
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.monitor", "Монитор нажатий…"), null, (_, _) => ShowMonitor()) { ShortcutKeys = Keys.F8 });
        _wizardItem.Click += (_, _) => ShowWizard(null);
        device.DropDownItems.Add(_wizardItem);
        device.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.refresh", "Обновить состояние"), null, (_, _) => RefreshDevice()));
        device.DropDownItems.Add(new ToolStripSeparator());
        device.DropDownItems.Add(BuildDriverMenu());
        for (int n = 1; n <= Profile.MaxLayers; n++)
        {
            int count = n;
            var item = new ToolStripMenuItem(n.ToString(), null, (_, _) => SetLayerCount(count));
            _layerItems.Add(item);
            _layersMenu.DropDownItems.Add(item);
        }
        device.DropDownItems.Add(_layersMenu);

        // «Настройки» — не список, а страница в этом же окне.
        var options = new ToolStripMenuItem(L.T("menu.settings", "Настройки"), null, (_, _) => ShowSettings());

        var help = new ToolStripMenuItem(L.T("menu.help", "Справка"));
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.howItWorks", "Как это работает"), null, (_, _) => StartTour()) { ShortcutKeys = Keys.F1 });
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.setup", "Мастер первого запуска…"), null, (_, _) => ShowSetup()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.updates", "Проверить обновления и файлы"), null, async (_, _) => await UpdateDialog.RunAsync(this)));
        // Окна, которые добавил скрипт текущей темы.
        if (ThemeScripts.Windows.Count > 0)
        {
            help.DropDownItems.Add(new ToolStripSeparator());
            foreach (var window in ThemeScripts.Windows)
                help.DropDownItems.Add(new ToolStripMenuItem(window.Menu, null, (_, _) => ThemeScripts.OpenWindow(window, this)));
        }
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.about", "О программе"), null, (_, _) => ShowAbout()));

        _menu.Items.AddRange(new ToolStripItem[] { file, device, options, help });
    }

    ToolStripMenuItem BuildDriverMenu()
    {
        var driver = new ToolStripMenuItem(L.T("menu.driver", "Драйвер"));
        if (driver.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowCheckMargin = true;
            dropDown.ImageScalingSize = new Size(Theme.S(28), Theme.S(20));
        }
        _autoDriverItem.Click += (_, _) => SetDriver(null);
        driver.DropDownItems.Add(_autoDriverItem);
        driver.DropDownItems.Add(new ToolStripSeparator());
        foreach (var def in DriverStore.All)
        {
            string id = def.Id;
            var item = new ToolStripMenuItem(def.Title, DriverIcon.Draw(def, Theme.S(28), Theme.S(20)), (_, _) => SetDriver(id))
            {
                ToolTipText = string.Join(", ", def.Usb) + " · " + UiText.ProtocolName(def.Protocol),
            };
            _driverItems[id] = item;
            driver.DropDownItems.Add(item);
        }
        driver.DropDownItems.Add(new ToolStripSeparator());
        driver.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.driver.folder", "Открыть папку драйверов"), null, (_, _) => OpenFolder(AppPaths.Drivers)));
        return driver;
    }

    Control BuildHeader()
    {
        // Фон цвета рамки + отступ в 1 пиксель снизу = тонкая линия под шапкой.
        var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(66), BackColor = Theme.Border, Padding = new Padding(0, 0, 0, 1) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S(18), 0, Theme.S(18), 0),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _deviceDot.Size = new Size(Theme.S(12), Theme.S(12));
        _deviceDot.Anchor = AnchorStyles.Left;
        _deviceDot.Margin = new Padding(0, 0, Theme.S(8), Theme.S(16));

        // Под «Устройство подключено» — записан ли текущий пресет в устройство.
        _deviceText.AutoSize = true;
        _deviceText.Font = Theme.Strong;
        _deviceText.ForeColor = Theme.Text;
        _deviceText.Location = new Point(0, 0);
        _writeState.AutoSize = true;
        _writeState.Font = Theme.Small;
        _writeState.Location = new Point(0, Theme.S(21));
        _deviceColumn.AutoSize = true;
        _deviceColumn.Anchor = AnchorStyles.Left;
        _deviceColumn.Margin = new Padding(0);
        _deviceColumn.Controls.Add(_deviceText);
        _deviceColumn.Controls.Add(_writeState);

        _presetLabel.Text = L.T("header.preset", "Пресет:");
        _presetLabel.AutoSize = true;
        _presetLabel.Anchor = AnchorStyles.Right;
        _presetLabel.ForeColor = Theme.Muted;
        _presetLabel.Margin = new Padding(Theme.S(12), 0, Theme.S(6), 0);

        _presetBox.Width = Theme.S(180);
        _presetBox.MaxDropDownItems = 16;
        _presetBox.Anchor = AnchorStyles.Left;
        _presetBox.Margin = new Padding(0, 0, Theme.S(4), 0);
        _presetBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressPresetEvent || _presetBox.SelectedItem is not string name) return;
            SwitchPreset(name);
        };

        StyleIconButton(_savePreset, Theme.IconGlyph("", "💾"));
        _savePreset.Click += (_, _) => QuickSave();
        StyleIconButton(_presetMore, Theme.IconGlyph("", "⋯"));
        _presetMore.Margin = new Padding(0, 0, Theme.S(16), 0);
        _presetMenu.Items.Add(new ToolStripMenuItem(L.T("menu.preset.saveAs", "Сохранить пресет как…"), null, (_, _) => SavePresetAs()));
        _presetMenu.Items.Add(new ToolStripMenuItem(L.T("menu.preset.refresh", "Обновить список"), null, (_, _) => RefreshPresets()));
        _presetMenu.Items.Add(new ToolStripMenuItem(L.T("menu.preset.folder", "Открыть папку с пресетами"), null, (_, _) => OpenFolder(AppPaths.Presets)));
        _presetMore.Click += (_, _) => _presetMenu.Show(_presetMore, new Point(0, _presetMore.Height));
        _tips.SetToolTip(_presetMore, L.T("header.presetMore", "Ещё действия с пресетами"));

        _layerLabel.Text = L.T("header.layer", "Слой:");
        _layerLabel.AutoSize = true;
        _layerLabel.Anchor = AnchorStyles.Right;
        _layerLabel.ForeColor = Theme.Muted;
        _layerLabel.Margin = new Padding(0, 0, Theme.S(6), 0);

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
        grid.Controls.Add(_deviceColumn, 1, 0);
        grid.Controls.Add(new Panel { Margin = new Padding(0), Size = new Size(1, 1) }, 2, 0);
        grid.Controls.Add(_presetLabel, 3, 0);
        grid.Controls.Add(_presetBox, 4, 0);
        grid.Controls.Add(_savePreset, 5, 0);
        grid.Controls.Add(_presetMore, 6, 0);
        grid.Controls.Add(_layerLabel, 7, 0);
        grid.Controls.Add(_layerBox, 8, 0);
        grid.Controls.Add(_writeButton, 9, 0);
        header.Controls.Add(grid);
        return header;
    }

    static void StyleIconButton(Button button, string glyph)
    {
        button.Text = glyph;
        button.Font = Theme.IconFont;
        button.Size = new Size(Theme.S(32), Theme.S(32));
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Hover;
        button.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
        button.BackColor = Theme.Surface;
        button.ForeColor = Theme.Muted;
        button.Cursor = Cursors.Hand;
        button.Anchor = AnchorStyles.Left;
        button.Margin = new Padding(0, 0, Theme.S(2), 0);
        button.UseMnemonic = false;
    }

    Control BuildBody()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Window,
            // У темы со своим фоном промежутки шире, чтобы фон было видно.
            Padding = new Padding(Theme.S(ThemeBackdrop.Active ? 30 : 16)),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(470)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = Card(_pad);
        left.Margin = new Padding(0, 0, Theme.S(ThemeBackdrop.Active ? 30 : 16), 0);
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
        _statusStrip.SizingGrip = false;
        _statusStrip.BackColor = Theme.Surface;
        _statusStrip.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        _statusStrip.Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0);
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Theme.Text;
        _driverStatus.ForeColor = Theme.Muted;
        _driverStatus.Margin = new Padding(Theme.S(12), 0, Theme.S(12), 0);
        _agentStatus.ForeColor = Theme.Muted;
        _statusStrip.Items.Add(_status);
        _statusStrip.Items.Add(_driverStatus);
        _statusStrip.Items.Add(_agentStatus);
        return _statusStrip;
    }

    /// <summary>Меню значка в трее (пересоздаётся вместе с интерфейсом — на нём подписи текущего языка).</summary>
    void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem(L.F("tray.open", "Открыть {0}", AppName), null, (_, _) => ShowFromTray());
        open.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.write", "Записать в устройство"), null, async (_, _) => await WriteToDeviceAsync()));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.monitor", "Монитор нажатий…"), null, (_, _) => ShowMonitor()));
        menu.Items.Add(ServicePresetMenu(_settings.ServicePreset, preset =>
        {
            _settings.ServicePreset = preset;
            SaveNow();
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.exit", "Выход"), null, (_, _) => ExitApp()));

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    /// <summary>Подменю «Пресет службы»: «Активный пресет» или любой сохранённый; отмечен выбранный.</summary>
    internal static ToolStripMenuItem ServicePresetMenu(string? current, Action<string?> pick)
    {
        var menu = new ToolStripMenuItem(L.T("settings.service.preset", "Пресет службы"));
        void Fill()
        {
            menu.DropDownItems.Clear();
            menu.DropDownItems.Add(new ToolStripMenuItem(L.T("settings.service.active", "Активный пресет"), null, (_, _) => { current = null; pick(null); })
                { Checked = current == null });
            menu.DropDownItems.Add(new ToolStripSeparator());
            foreach (var name in PresetStore.Names())
                menu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => { current = name; pick(name); }) { Checked = current == name });
        }
        Fill();
        // Список пресетов мог измениться — перечитываем при каждом открытии.
        menu.DropDownOpening += (_, _) => Fill();
        return menu;
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
        if (_snapshotPath == null)
        {
            // Первый запуск: мастер настройки страницей в окне, после него — тур, затем предложение включить службу.
            if (!_settings.SetupDone && Visible) BeginInvoke(ShowSetup);
            else if (!_settings.TourDone && Visible) BeginInvoke(StartTour);
            else if (Visible) BeginInvoke(OfferService);
            return;
        }

        // Режим --snapshot: сохранить картинку окна и выйти (для проверки вёрстки).
        await Task.Delay(500);
        if (IsDisposed) return;
        if (SnapshotSwitch.Theme != null || SnapshotSwitch.Language != null)
        {
            _settings.Theme = SnapshotSwitch.Theme ?? _settings.Theme;
            _settings.Language = SnapshotSwitch.Language ?? _settings.Language;
            ApplyAppearance();
            await Task.Delay(300);
        }
        if (SnapshotPage != null)
        {
            ShowPage(SnapshotPage switch
            {
                "about" => new AboutPage(_settings, Transition.Capture(this, null)),
                "setup" => new SetupPage(_settings),
                _ => WiredSettings(new SettingsPage(_settings, null)),
            });
            await Task.Delay(400);
            if (SnapshotPreviewTheme != null && _pages.TryPeek(out var top) && top is SettingsPage settingsPage)
            {
                settingsPage.PickTheme(SnapshotPreviewTheme);
                await Task.Delay(700);
            }
        }
        if (SnapshotTourStep >= 0)
        {
            StartTour(force: true);
            _tour?.Go(SnapshotTourStep);
            await Task.Delay(300);
            // DrawToBitmap рисует дочерние элементы в обратном порядке — слой тура снимаем отдельно.
            if (_tour != null)
            {
                using var overlay = new Bitmap(_tour.Width, _tour.Height);
                _tour.DrawToBitmap(overlay, new Rectangle(Point.Empty, _tour.Size));
                overlay.Save(_snapshotPath, ImageFormat.Png);
                _exiting = true;
                Close();
                return;
            }
        }
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
        if (!_settings.SetupDone) BeginInvoke(ShowSetup);
        else if (!_settings.TourDone && _tour == null) BeginInvoke(StartTour);
    }

    void ShowMonitor()
    {
        if (_monitor is { IsDisposed: false })
        {
            _monitor.Activate();
            return;
        }
        _monitor = new MonitorForm
        {
            // Монитор подписывает события платы названием кнопки, которой назначено такое действие.
            FindControl = match => _settings.Controls.Where(id => match(CurrentLayer.Get(id))).Select(UiText.Title).FirstOrDefault(),
        };
        _monitor.FormClosed += (_, _) => _monitor = null;
        if (Visible) _monitor.Show(this);
        else _monitor.Show();
    }

    void ExitApp()
    {
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
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _deviceTimer.Dispose();
        _saveTimer.Dispose();
        _tips.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Устройство подключили или отключили — проверяем с небольшой задержкой, пока Windows его настроит.
        if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            _deviceTimer.Stop();
            _deviceTimer.Start();
            // Страница проверки записи ждёт переподключения клавиатуры, а это сообщение получают только окна.
            foreach (var page in _pages.OfType<WriteCheckPage>()) page.DeviceChanged();
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Пока открыта страница, горячие клавиши скрытого меню (F5, Ctrl+S…) не срабатывают.
        if (_pages.Count > 0) return false;
        return base.ProcessCmdKey(ref msg, keyData);
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

    /// <summary>Действие перетащили на другую кнопку: перенос, а с Ctrl — копия. Занятую кнопку сначала спрашиваем.</summary>
    void OnActionDropped(ActionDrop drop)
    {
        var layer = CurrentLayer;
        var source = layer.Get(drop.Source);
        var target = layer.Get(drop.Target);
        if (target.Kind != ActionKind.None && _settings.ConfirmOverwrite)
        {
            var (button, dontAsk) = ThemedDialog.Show(this, AppName,
                L.T("drop.overwrite.heading", "Внимание, вы хотите перезаписать комбинацию на клавише!"),
                L.F("drop.overwrite.text", "Сейчас на «{0}»: {1}\nБудет: {2}", UiText.Title(drop.Target), BindingText.Summary(target), BindingText.Summary(source)),
                new[] { L.T("drop.overwrite", "Перезаписать"), L.T("button.cancel", "Отмена") }, DialogKind.Warning,
                L.T("drop.dontAsk", "Больше не показывать это сообщение"), cancelIndex: 1);
            if (button != 0) return;
            if (dontAsk) _settings.ConfirmOverwrite = false;
        }

        var moved = Clone(source);
        if (drop.Copy) moved.TriggerSlot = null;
        layer.Actions[drop.Target] = moved;
        if (!drop.Copy) layer.Actions[drop.Source] = new PadAction();
        AfterProfileReplaced();
        SelectControl(drop.Target);
        _pad.Flash(drop.Target);
        SetStatus(drop.Copy
            ? L.F("status.copied", "Действие скопировано: {0} → {1}.", UiText.Title(drop.Source), UiText.Title(drop.Target))
            : L.F("status.moved", "Действие перенесено: {0} → {1}.", UiText.Title(drop.Source), UiText.Title(drop.Target)));
    }

    static PadAction Clone(PadAction action) =>
        JsonSerializer.Deserialize<PadAction>(JsonSerializer.Serialize(action, SettingsStore.Json), SettingsStore.Json)!;

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

    /// <summary>
    /// Проверка записи (только платы прямого формата): страницей в окне. Из мастера первого запуска —
    /// поверх него, и по окончании возвращаемся в мастер.
    /// </summary>
    void ShowWizard(SetupPage? setup)
    {
        if (_settings.EffectiveProtocol != ProtocolMode.Direct || _tour != null || _pages.Any(p => p is WriteCheckPage)) return;
        if (setup == null && _pages.Count > 0) return;
        // Монитор тоже читает сырой ввод — на время проверки закрываем его.
        _monitor?.Close();
        if (!Visible) ShowFromTray();
        var page = new WriteCheckPage(_settings.EffectiveDriver);
        page.Finished += async (_, _) =>
        {
            ClosePage();
            // В клавиатуре теперь тестовые буквы — настройки придётся записать заново.
            _settings.LastWrittenSignature = null;
            var driver = _settings.EffectiveDriver;
            if (page.Result is ProtocolMode.Direct && page.KnobSlots != null)
            {
                _settings.LearnedKnobSlots ??= new();
                _settings.LearnedKnobSlots[driver.Id] = page.KnobSlots;
            }
            if (setup != null)
            {
                setup.Rebuild();
                ScheduleSave();
                return;
            }
            if (page.Result is not ProtocolMode.Direct)
            {
                UpdateDirty();
                ScheduleSave();
                return;
            }

            _settings.DriverId = driver.Id;
            AfterDriverChanged();
            RefreshDevice();
            var (button, _) = ThemedDialog.Show(this, AppName, L.F("wizard.apply.heading", "Драйвер «{0}» сохранён", driver.Title),
                L.T("wizard.apply.text", "Записать в клавиатуру ваши настройки?"),
                new[] { L.T("header.write", "Записать в устройство"), L.T("button.later", "Позже") }, DialogKind.Question, cancelIndex: 1);
            if (button == 0) await WriteToDeviceAsync();
        };
        ShowPage(page);
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
        UiText.Driver = _settings.EffectiveDriver;
        _pad.Driver = _settings.EffectiveDriver;
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
        _layersMenu.Enabled = Execution.SupportsLayers(_settings.EffectiveProtocol);
        _layersMenu.ToolTipText = _layersMenu.Enabled ? "" : L.T("menu.layers.unsupported", "Этот драйвер не поддерживает слои");
        _wizardItem.Enabled = _settings.EffectiveProtocol == ProtocolMode.Direct;
        _wizardItem.ToolTipText = _wizardItem.Enabled ? "" : L.T("menu.wizard.directOnly", "Только для плат прямого формата");
    }

    /// <summary>Статус под «Устройство подключено» и подсветка кнопки сохранения пресета.</summary>
    void UpdateDirty()
    {
        _settings.EnsureTriggers();
        string signature = ProfileCompiler.Signature(ProfileCompiler.Compile(_settings));
        if (_settings.LastWrittenSignature == null)
        {
            _writeState.Text = L.T("header.notWritten", "● Ещё не записано в устройство");
            _writeState.ForeColor = Theme.Warning;
        }
        else if (signature != _settings.LastWrittenSignature)
        {
            _writeState.Text = L.T("header.unsaved", "● Есть изменения, не записанные в устройство");
            _writeState.ForeColor = Theme.Warning;
        }
        else
        {
            _writeState.Text = L.T("header.written", "✓ Записано в устройство");
            _writeState.ForeColor = Theme.Success;
        }

        bool presetDirty = PresetDirty;
        _savePreset.ForeColor = presetDirty ? Theme.Accent : Theme.Muted;
        _tips.SetToolTip(_savePreset, presetDirty
            ? L.T("header.save.dirty", "Сохранить пресет (Ctrl+S) — есть несохранённые изменения")
            : L.T("header.save", "Сохранить пресет (Ctrl+S)"));
    }

    bool PresetDirty => SettingsStore.Snapshot(WithName(_settings.Profile)) != _savedSnapshot;

    Profile WithName(Profile profile)
    {
        profile.Name = _settings.ActivePreset ?? "";
        return profile;
    }

    async Task WriteToDeviceAsync()
    {
        if (_writing) return;
        RefreshDevice();
        if (_device is not { } device)
        {
            ThemedDialog.Show(this, AppName, L.T("write.noDevice.heading", "Макро-клавиатура не найдена"),
                L.T("write.noDevice.text", "Подключите её USB-кабелем (по Bluetooth запись не работает) и попробуйте ещё раз."),
                new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
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
            ThemedDialog.Show(this, AppName, L.T("write.error.heading", "Не удалось записать настройки в устройство"),
                L.F("write.error.text", "{0}\n\nПереподключите клавиатуру и попробуйте ещё раз.", ex.Message),
                new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
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
        _settings.Profile.All(_settings.EffectiveLayerCount, _settings.Controls)
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
        if (!_holdFilter.ShouldRun(slot, control, action))
        {
            _monitor?.LogAgent(BindingText.Summary(action), $"{UiText.Title(control)} · {L.T("monitor.heldSkip", "удержание — повтор пропущен")}");
            return;
        }
        _monitor?.LogAgent(BindingText.Summary(action), $"{UiText.Title(control)} · {TriggerKeys.Describe(slot)}");
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
                ThemedDialog.Show(this, AppName, title, ex.Message, new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
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

    // ───────────────────────── Страницы в окне ─────────────────────────

    /// <summary>Показывает страницу на месте основного содержимого окна (меню, шапка и схема прячутся).</summary>
    void ShowPage(Control page) => Transition.Run(this, () =>
    {
        SuspendLayout();
        if (_pages.TryPeek(out var top)) top.Visible = false;
        else HideMain();
        _pages.Push(page);
        // Страницы раскладывают содержимое по своей ширине при первом показе — размер задаём до добавления.
        page.Bounds = ClientRectangle;
        page.Dock = DockStyle.Fill;
        Controls.Add(page);
        page.BringToFront();
        ResumeLayout(true);
        page.Select();
    });

    void HideMain()
    {
        _hiddenMain = Controls.Cast<Control>().Where(c => c.Visible && !_pages.Contains(c) && !Transition.IsCover(c)).ToList();
        foreach (var control in _hiddenMain) control.Visible = false;
    }

    /// <summary>Закрывает верхнюю страницу: снова видна предыдущая страница или основное содержимое.</summary>
    void ClosePage()
    {
        if (!_pages.TryPop(out var page)) return;
        Transition.Run(this, () =>
        {
            SuspendLayout();
            Controls.Remove(page);
            if (_pages.TryPeek(out var top)) top.Visible = true;
            else
            {
                foreach (var control in _hiddenMain.Where(c => !c.IsDisposed)) control.Visible = true;
                _hiddenMain.Clear();
            }
            ResumeLayout(true);
        });
        // Страницу закрывают из её же обработчика щелчка — освобождаем после выхода из него.
        BeginInvoke(page.Dispose);
    }

    /// <summary>Тема или язык сменились: перечитать ресурсы и перестроить содержимое этого же окна.</summary>
    void ApplyAppearance() => Transition.Run(this, () =>
    {
        Notices.Clear();
        Program.LoadResources(_settings);
        _settings.Normalize();
        BuildUi(_pad.Selected);
        BackColor = Theme.Window;
        if (IsHandleCreated) Theme.DarkTitleBar(this);
    });

    void ShowSettings()
    {
        if (_tour != null || _pages.Count > 0) return;
        var page = new SettingsPage(_settings, _device != null ? (_device.VendorId, _device.ProductId) : null);
        WireSettings(page);
        ShowPage(page);
    }

    SettingsPage WiredSettings(SettingsPage page)
    {
        WireSettings(page);
        return page;
    }

    void WireSettings(SettingsPage page)
    {
        page.AppearancePreview += (_, _) => BeginInvoke(() => PreviewAppearance(page));
        page.Cancelled += (_, _) =>
        {
            ClosePage();
            // «Отмена» возвращает тему и язык, какими они были при открытии настроек.
            if (ThemeStore.Current.Id != page.OriginalTheme || L.Code != page.OriginalLanguage)
            {
                _settings.Theme = page.OriginalTheme;
                _settings.Language = page.OriginalLanguage;
                ApplyAppearance();
                SaveNow();
            }
        };
        page.Done += (_, _) =>
        {
            ClosePage();
            if (page.DriverChanged) AfterDriverChanged();
            if (page.AppearanceChanged) ApplyAppearance();
            RefreshDevice();
            SaveNow();
            if (page.RerunSetup) BeginInvoke(ShowSetup);
            else if (page.ShowTour) BeginInvoke(StartTour);
        };
    }

    /// <summary>В настройках выбрали тему или язык: применяем сразу и заменяем страницу такой же, но в новом оформлении.</summary>
    void PreviewAppearance(SettingsPage page)
    {
        if (!_pages.TryPeek(out var top) || top != page) return;
        Transition.Run(this, () =>
        {
            _settings.Theme = page.PendingTheme;
            _settings.Language = page.PendingLanguage;
            ApplyAppearance();
            var fresh = page.Recreate();
            WireSettings(fresh);
            SuspendLayout();
            _pages.Pop();
            Controls.Remove(page);
            _pages.Push(fresh);
            fresh.Bounds = ClientRectangle;
            fresh.Dock = DockStyle.Fill;
            Controls.Add(fresh);
            fresh.BringToFront();
            ResumeLayout(true);
            fresh.Select();
        });
        BeginInvoke(page.Dispose);
    }

    void ShowAbout()
    {
        if (_tour != null || _pages.Count > 0) return;
        var page = new AboutPage(_settings, Transition.Capture(this, null));
        page.Closed += (_, _) => ClosePage();
        ShowPage(page);
    }

    /// <summary>Мастер первого запуска (язык, тема, плата) — страницей в окне; язык и тема применяются сразу.</summary>
    void ShowSetup()
    {
        if (!_interactive || _tour != null || _pages.Count > 0) return;
        _settings.SetupDone = false;
        SaveNow();
        var page = new SetupPage(_settings);
        page.AppearanceChanged += (_, _) =>
        {
            // Мастер перерисуется сам; окну остаётся сменить фон и цвет заголовка.
            BackColor = Theme.Window;
            Theme.DarkTitleBar(this);
            ThemeBackdrop.Update(this);
            ScheduleSave();
        };
        page.WriteCheckRequested += (_, _) => ShowWizard(page);
        page.Finished += (_, _) =>
        {
            ClosePage();
            _layer = 0;
            ApplyAppearance();
            SaveNow();
            BeginInvoke(StartTour);
        };
        ShowPage(page);
    }

    /// <summary>Тур по окну: подсвечивает области и объясняет, что они делают.</summary>
    void StartTour() => StartTour(force: false);

    void StartTour(bool force)
    {
        if (_tour != null || _pages.Count > 0 || !Visible || (!_interactive && !force)) return;
        Rectangle Of(Control control, Rectangle? inner = null)
        {
            var rect = inner ?? new Rectangle(Point.Empty, control.Size);
            return RectangleToClient(control.RectangleToScreen(rect));
        }

        var presetArea = Rectangle.Union(Of(_presetLabel), Of(_presetMore));
        var steps = new List<TourStep>
        {
            new(Of(_pad, _pad.PadBounds), L.T("tour.pad.title", "Ваша клавиатура"),
                L.T("tour.pad.text", "Щёлкните клавишу или ручку, чтобы настроить её. Перетащите клавишу на другую — действие переедет, а с зажатым Ctrl скопируется.")),
            new(Of(_pad, _pad.ListBounds), L.T("tour.list.title", "Все кнопки"),
                L.T("tour.list.text", "Тот же выбор списком: у каждой кнопки видно, что она делает. ⚙ — действие выполняет программа в фоне. Сюда тоже можно перетаскивать.")),
            new(Of(_editor), L.T("tour.editor.title", "Что делает кнопка"),
                L.T("tour.editor.text", "Выберите тип действия: сочетание клавиш, мультимедиа, мышь или системное действие. Зелёная плашка ✓ — записывается в саму клавиатуру, жёлтая ⚙ — работает, пока запущена программа.")),
            new(Of(_deviceColumn), L.T("tour.device.title", "Состояние устройства"),
                L.T("tour.device.text", "Подключена ли клавиатура и записан ли в неё текущий пресет.")),
            new(presetArea, L.T("tour.preset.title", "Пресеты"),
                L.T("tour.preset.text", "Раскладки хранятся пресетами. Переключайте их здесь, сохраняйте кнопкой с дискетой (Ctrl+S); в «⋯» — «Сохранить как», обновление списка и папка с пресетами.")),
            new(Of(_writeButton), L.T("tour.write.title", "Запись в устройство"),
                L.T("tour.write.text", "Отправляет настройки в клавиатуру по USB (F5). После записи переподключите кабель.")),
            new(Of(_menu), L.T("tour.menu.title", "Меню"),
                L.T("tour.menu.text", "«Устройство» — монитор нажатий, драйвер платы и слои. «Настройки» — тема, язык, автозапуск. «Справка» — этот тур и сведения о программе.")),
            new(Of(_statusStrip), L.T("tour.status.title", "Строка состояния"),
                L.T("tour.status.text", "Подсказки, текущий драйвер и сколько действий выполняет программа в фоне.")),
        };

        _tour = new TourOverlay(this, steps);
        _tour.Finished += (_, _) =>
        {
            _tour = null;
            if (!_settings.TourDone)
            {
                _settings.TourDone = true;
                ScheduleSave();
            }
            BeginInvoke(OfferService);
        };
        _tour.Start();
    }

    /// <summary>
    /// Фоновая служба не запущена: установлена — просто запускаем, нет — спрашиваем разрешения на её автозапуск.
    /// </summary>
    void OfferService()
    {
        if (!_interactive || OmkService.IsRunning) return;
        try
        {
            if (OmkService.IsInstalled)
            {
                OmkService.RefreshPathIfInstalled();
                return; // служба запустится сама, как только программа закроется
            }
            if (_settings.ServicePromptDismissed) return;
            var (button, dontAsk) = ThemedDialog.Show(this, AppName,
                L.F("service.offer.heading", "Включить фоновую службу {0}?", OmkService.Name),
                L.T("service.offer.text", "Тогда громкость, треки и другие действия кнопок будут работать, даже когда программа закрыта. Служба запускается вместе с Windows и не показывает окон. Удалить или переустановить её можно в «Настройки → Фоновая служба»."),
                new[] { L.T("service.offer.allow", "Разрешить"), L.T("button.later", "Позже") }, DialogKind.Question,
                L.T("drop.dontAsk", "Больше не показывать это сообщение"), cancelIndex: 1);
            if (button == 0)
            {
                OmkService.Install();
                // Пока открыта программа, служба ждёт; клавиши остаются у программы.
                OmkService.RequestPause();
                ApplyAgent();
                SetStatus(L.F("status.serviceInstalled", "Служба {0} установлена: действия кнопок работают и при закрытой программе.", OmkService.Name));
            }
            else if (dontAsk)
            {
                _settings.ServicePromptDismissed = true;
                ScheduleSave();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            SetStatus(ex.Message);
        }
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
            ThemedDialog.Show(this, AppName, ex.Message, path, new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
        }
    }

    // ───────────────────────── Пресеты ─────────────────────────

    /// <summary>Сохранённое содержимое пресета — с ним сравнивается рабочая копия.</summary>
    string SavedSnapshotOf(string? name)
    {
        if (name == null || !PresetStore.Exists(name)) return "";
        try
        {
            var saved = PresetStore.Load(name);
            saved.Name = name;
            saved.EnsureTriggers(_settings.NeedsAgent, _settings.Controls);
            return SettingsStore.Snapshot(saved);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return "";
        }
    }

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
        UpdateDirty();
    }

    /// <summary>Делает пресет активным: его содержимое — в рабочую копию.</summary>
    void ActivatePreset(string name, Profile profile)
    {
        _settings.Profile = profile;
        _settings.ActivePreset = name;
        _settings.EnsureTriggers();
        _savedSnapshot = SavedSnapshotOf(name);
        AfterProfileReplaced();
        SaveNow();
        RefreshPresets();
    }

    /// <summary>Записывает рабочую копию в файл пресета <paramref name="name"/> и делает его активным.</summary>
    void WritePreset(string name)
    {
        PresetStore.Save(name, _settings.Profile);
        _settings.ActivePreset = name;
        _savedSnapshot = SavedSnapshotOf(name);
        SaveNow();
        RefreshPresets();
        SetStatus(L.F("status.presetSaved", "Пресет «{0}» сохранён.", name));
    }

    /// <summary>Есть несохранённые изменения — спрашивает. False — пользователь передумал.</summary>
    bool ConfirmLeavePreset()
    {
        if (!PresetDirty || _settings.ActivePreset is not string current) return true;
        var (button, _) = ThemedDialog.Show(this, AppName, L.F("preset.unsaved.heading", "Сохранить изменения в пресете «{0}»?", current),
            L.T("preset.unsaved.text", "Если не сохранить, изменения пропадут."),
            new[] { L.T("button.save", "Сохранить"), L.T("button.dontSave", "Не сохранять"), L.T("button.cancel", "Отмена") },
            DialogKind.Question, cancelIndex: 2);
        if (button == 0) RunPresetCommand(() => WritePreset(current));
        return button is 0 or 1;
    }

    void SwitchPreset(string name)
    {
        if (name == _settings.ActivePreset) return;
        if (!ConfirmLeavePreset())
        {
            RefreshPresets();
            return;
        }
        try
        {
            ActivatePreset(name, PresetStore.Load(name));
            SetStatus(L.F("status.presetSwitched", "Пресет «{0}». Нажмите «Записать в устройство», чтобы применить его.", name));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(this, AppName, L.F("preset.loadError.heading", "Не удалось открыть пресет «{0}»", name), ex.Message,
                new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
            RefreshPresets();
        }
    }

    /// <summary>
    /// Быстрое сохранение: файл с таким названием уже есть — спрашиваем, дать новое название или перезаписать.
    /// </summary>
    void QuickSave()
    {
        string name = _settings.ActivePreset ?? L.T("preset.default", "Основной");
        if (!PresetStore.Exists(name))
        {
            RunPresetCommand(() => WritePreset(name));
            return;
        }
        var (button, _) = ThemedDialog.Show(this, AppName, L.F("preset.exists.heading", "Пресет «{0}» уже есть", name),
            L.T("preset.exists.text", "Файл с таким названием уже лежит в папке пресетов. Дайте пресету новое название или перезапишите существующий."),
            new[] { L.T("preset.newName", "Дать новое название"), L.T("preset.overwrite", "Перезаписать"), L.T("button.cancel", "Отмена") },
            DialogKind.Question, cancelIndex: 2);
        if (button == 0) SavePresetAs();
        else if (button == 1) RunPresetCommand(() => WritePreset(name));
    }

    /// <summary>Спрашивает название пресета. Null — отмена.</summary>
    string? AskPresetName(string title, string prompt, string initial, string? current = null) =>
        InputDialog.Ask(this, title, prompt, initial, text =>
        {
            string name = PresetStore.Clean(text);
            if (name.Length == 0) return L.T("preset.nameEmpty", "Введите название.");
            bool same = current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            return PresetStore.Exists(name) && !same ? L.F("preset.taken", "Пресет «{0}» уже есть — выберите другое название.", name) : null;
        }) is string answer ? PresetStore.Clean(answer) : null;

    void SavePresetAs()
    {
        string? name = AskPresetName(L.T("preset.saveAs.title", "Сохранить пресет как"),
            L.T("preset.saveAs.prompt", "Название нового пресета:"),
            PresetStore.Unique(_settings.ActivePreset ?? L.T("preset.default", "Основной")));
        if (name != null) RunPresetCommand(() => WritePreset(name));
    }

    void NewPreset()
    {
        if (!ConfirmLeavePreset()) return;
        string? name = AskPresetName(L.T("preset.new.title", "Новый пресет"),
            L.T("preset.new.prompt", "Название нового пресета. Он начнётся с примера настроек."),
            PresetStore.Unique(L.T("preset.new.name", "Новый пресет")));
        if (name == null) return;
        RunPresetCommand(() =>
        {
            var profile = Profile.CreateDefault();
            PresetStore.Save(name, profile);
            ActivatePreset(name, profile);
        });
    }

    void RenamePreset()
    {
        string? current = _settings.ActivePreset;
        if (current == null) return;
        string? name = AskPresetName(L.T("preset.rename.title", "Переименовать пресет"),
            L.T("preset.rename.prompt", "Новое название пресета:"), current, current);
        if (name == null || name == current) return;
        RunPresetCommand(() =>
        {
            if (PresetStore.Exists(current)) PresetStore.Rename(current, name);
            _settings.ActivePreset = name;
            _savedSnapshot = SavedSnapshotOf(name);
            SaveNow();
            RefreshPresets();
        });
    }

    void DeletePreset()
    {
        string? current = _settings.ActivePreset;
        if (current == null || PresetStore.Names().Count <= 1) return;
        var (button, _) = ThemedDialog.Show(this, AppName, L.F("preset.delete.heading", "Удалить пресет «{0}»?", current),
            L.T("preset.delete.text", "Файл пресета будет удалён. Это нельзя отменить."),
            new[] { L.T("button.delete", "Удалить"), L.T("button.cancel", "Отмена") }, DialogKind.Warning, cancelIndex: 1);
        if (button != 0) return;
        RunPresetCommand(() =>
        {
            PresetStore.Delete(current);
            string next = PresetStore.Names()[0];
            ActivatePreset(next, PresetStore.Load(next));
        });
    }

    void ImportPreset()
    {
        if (!ConfirmLeavePreset()) return;
        using var dialog = new OpenFileDialog
        {
            Title = L.T("preset.import.title", "Импорт пресета"),
            Filter = L.T("filter.preset", "Пресет OshiMakiKey (*.json)|*.json") + "|" + L.T("filter.all", "Все файлы (*.*)|*.*"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
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
            SettingsStore.WriteProfile(WithName(_settings.Profile), dialog.FileName);
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
            ThemedDialog.Show(this, AppName, L.T("preset.error.heading", "Не удалось выполнить действие с пресетом"), ex.Message,
                new[] { L.T("button.ok", "ОК") }, DialogKind.Warning, cancelIndex: 0);
            RefreshPresets();
        }
    }

    void ClearLayer()
    {
        var (button, _) = ThemedDialog.Show(this, AppName, L.T("clear.heading", "Очистить текущий слой?"),
            L.T("clear.text", "Все действия этого слоя станут «Нет действия»."),
            new[] { L.T("button.clear", "Очистить"), L.T("button.cancel", "Отмена") }, DialogKind.Question, cancelIndex: 1);
        if (button != 0) return;
        foreach (var id in _settings.Controls) CurrentLayer.Actions[id] = new PadAction();
        AfterProfileReplaced();
    }

    void ResetToDefault()
    {
        var (button, _) = ThemedDialog.Show(this, AppName, L.T("reset.heading", "Вернуть пример настроек?"),
            L.T("reset.text", "Все действия текущего пресета заменятся примером: копировать, вставить, отменить, музыка, громкость."),
            new[] { L.T("button.replace", "Заменить"), L.T("button.cancel", "Отмена") }, DialogKind.Question, cancelIndex: 1);
        if (button != 0) return;
        _settings.Profile = Profile.CreateDefault();
        AfterProfileReplaced();
    }

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
