using System.Drawing.Imaging;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Главное окно: схема устройства, редактор действия, запись в устройство, значок в трее и фоновый агент.</summary>
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
    int _layer;
    HidDeviceInfo? _device;
    HashSet<int> _registeredSlots = new();
    List<int> _failedSlots = new();
    RegisteredWaitHandle? _showWait;
    MonitorForm? _monitor;

    readonly DevicePanel _pad = new();
    readonly BindingEditor _editor = new();
    readonly StatusDot _deviceDot = new();
    readonly Label _deviceText = new();
    readonly Label _dirtyLabel = new();
    readonly Label _layerLabel = new();
    readonly ComboBox _layerBox = new();
    readonly Button _writeButton = new();
    readonly ToolStripStatusLabel _status = new();
    readonly ToolStripStatusLabel _agentStatus = new();
    readonly NotifyIcon _tray = new();
    readonly HotkeyAgent? _agent;
    readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 700 };
    readonly ToolStripMenuItem _autostartItem = new("Запускать вместе с Windows");
    readonly ToolStripMenuItem _trayItem = new("Сворачивать в трей при закрытии");
    readonly ToolStripMenuItem _layersMenu = new("Количество слоёв");
    readonly List<ToolStripMenuItem> _layerItems = new();
    readonly Dictionary<ProtocolMode, ToolStripMenuItem> _protocolItems = new();

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
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(1180), workArea.Width - 40), Math.Min(Theme.S(760), workArea.Height - 60));
        MinimumSize = new Size(Math.Min(Theme.S(1060), workArea.Width), Math.Min(Theme.S(700), workArea.Height));

        var menu = BuildMenu();
        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());
        Controls.Add(menu);
        MainMenuStrip = menu;
        BuildTray();

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
        RebuildLayerUi();
        UpdateProtocolMenu();
        SelectControl(select ?? ControlId.Key1);
        UpdateDirty();
        ApplyAgent();
        SetStatus("Выберите клавишу или действие ручки слева, настройте его справа и нажмите «Записать в устройство».");
    }

    Layer CurrentLayer => _settings.Profile.Layers[_layer];

    // ───────────────────────── Построение интерфейса ─────────────────────────

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Theme.Surface, Padding = new Padding(Theme.S(6), Theme.S(2), 0, Theme.S(2)) };

        var file = new ToolStripMenuItem("Файл");
        file.DropDownItems.Add(new ToolStripMenuItem("Открыть профиль…", null, (_, _) => OpenProfile()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem("Сохранить профиль как…", null, (_, _) => SaveProfileAs()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Очистить текущий слой", null, (_, _) => ClearLayer()));
        file.DropDownItems.Add(new ToolStripMenuItem("Вернуть пример настроек", null, (_, _) => ResetToDefault()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitApp()));

        var device = new ToolStripMenuItem("Устройство");
        device.DropDownItems.Add(new ToolStripMenuItem("Записать в устройство", null, async (_, _) => await WriteToDeviceAsync()) { ShortcutKeys = Keys.F5 });
        device.DropDownItems.Add(new ToolStripMenuItem("Монитор нажатий…", null, (_, _) => ShowMonitor()) { ShortcutKeys = Keys.F8 });
        device.DropDownItems.Add(new ToolStripMenuItem("Обновить состояние", null, (_, _) => RefreshDevice()));
        device.DropDownItems.Add(new ToolStripSeparator());

        device.DropDownItems.Add(new ToolStripMenuItem("Проверить запись…", null, (_, _) => ShowWizard()));
        var protocol = new ToolStripMenuItem("Способ записи");
        AddProtocolItem(protocol, ProtocolMode.Auto, "Определять автоматически");
        AddProtocolItem(protocol, ProtocolMode.Direct, "Прямой — CH552 6 клавиш + ручка");
        AddProtocolItem(protocol, ProtocolMode.Vendor, "Как у официальной программы (без префикса 03)");
        AddProtocolItem(protocol, ProtocolMode.Legacy, "Совместимый с ch57x-keyboard-tool");
        device.DropDownItems.Add(protocol);

        for (int n = 1; n <= Profile.MaxLayers; n++)
        {
            int count = n;
            var item = new ToolStripMenuItem(n.ToString(), null, (_, _) => SetLayerCount(count));
            _layerItems.Add(item);
            _layersMenu.DropDownItems.Add(item);
        }
        device.DropDownItems.Add(_layersMenu);

        var options = new ToolStripMenuItem("Настройки");
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _trayItem.Click += (_, _) =>
        {
            _settings.MinimizeToTray = !_settings.MinimizeToTray;
            _trayItem.Checked = _settings.MinimizeToTray;
            ScheduleSave();
        };
        options.DropDownItems.Add(_autostartItem);
        options.DropDownItems.Add(_trayItem);
        options.DropDownOpening += (_, _) =>
        {
            _autostartItem.Checked = Autostart.IsEnabled;
            _trayItem.Checked = _settings.MinimizeToTray;
        };

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add(new ToolStripMenuItem("Как это работает", null, (_, _) => ShowHowItWorks()));
        help.DropDownItems.Add(new ToolStripMenuItem("О программе", null, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, device, options, help });
        return menu;
    }

    void AddProtocolItem(ToolStripMenuItem parent, ProtocolMode mode, string text)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => SetProtocol(mode));
        _protocolItems[mode] = item;
        parent.DropDownItems.Add(item);
    }

    Control BuildHeader()
    {
        // Фон цвета рамки + отступ в 1 пиксель снизу = тонкая линия под шапкой.
        var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(66), BackColor = Theme.Border, Padding = new Padding(0, 0, 0, 1) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S(18), 0, Theme.S(18), 0),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

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

        _layerLabel.Text = "Слой:";
        _layerLabel.AutoSize = true;
        _layerLabel.Anchor = AnchorStyles.Right;
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

        _writeButton.Text = "Записать в устройство";
        _writeButton.Size = new Size(Theme.S(230), Theme.S(40));
        _writeButton.FlatStyle = FlatStyle.Flat;
        _writeButton.BackColor = Theme.Accent;
        _writeButton.ForeColor = Color.White;
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
        grid.Controls.Add(_layerLabel, 4, 0);
        grid.Controls.Add(_layerBox, 5, 0);
        grid.Controls.Add(_writeButton, 6, 0);
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
        var strip = new StatusStrip { SizingGrip = false, BackColor = Theme.Surface, Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0) };
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Theme.Text;
        _agentStatus.ForeColor = Theme.Muted;
        strip.Items.Add(_status);
        strip.Items.Add(_agentStatus);
        return strip;
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Открыть " + AppName, null, (_, _) => ShowFromTray());
        open.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem("Записать в устройство", null, async (_, _) => await WriteToDeviceAsync()));
        menu.Items.Add(new ToolStripMenuItem("Монитор нажатий…", null, (_, _) => ShowMonitor()));
        menu.Items.Add(new ToolStripMenuItem("Проверить запись…", null, (_, _) => ShowWizard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitApp()));

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
                _tray.ShowBalloonTip(5000, AppName + " работает в фоне",
                    "Действия клавиш, которые выполняет программа, продолжают работать. Чтобы выйти, щёлкните правой кнопкой по значку.",
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
        if (_device != null && MacroPadDevice.DetectProtocol() is { } detected && detected != _settings.DetectedProtocol)
        {
            _settings.DetectedProtocol = detected;
            AfterProtocolChanged();
        }

        if (_device != null)
        {
            _deviceDot.DotColor = Theme.Success;
            _deviceText.Text = $"Устройство подключено  ·  CH552 {_device.VendorId:X4}:{_device.ProductId:X4}  ·  {ProtocolTitle(_settings.EffectiveProtocol)}";
        }
        else
        {
            _deviceDot.DotColor = Theme.Danger;
            _deviceText.Text = "Устройство не найдено — подключите его USB-кабелем";
        }
    }

    static string ProtocolTitle(ProtocolMode mode) => mode switch
    {
        ProtocolMode.Legacy => "формат ch57x",
        ProtocolMode.Vendor => "как у официальной программы",
        _ => "прямой протокол",
    };

    async void ShowWizard()
    {
        _monitor?.Close();
        using var wizard = new ProtocolWizardForm();
        if (Visible) wizard.ShowDialog(this);
        else wizard.ShowDialog();

        // В клавиатуре теперь тестовые буквы — настройки придётся записать заново.
        _settings.LastWrittenSignature = null;
        if (wizard.Result is not ProtocolMode mode)
        {
            UpdateDirty();
            ScheduleSave();
            return;
        }

        _settings.Protocol = mode;
        if (wizard.KnobSlots != null) _settings.KnobSlots = wizard.KnobSlots;
        AfterProtocolChanged();
        RefreshDevice();
        if (MessageBox.Show(this, $"Способ записи «{ProtocolTitle(mode)}» сохранён.\n\nЗаписать в клавиатуру ваши настройки?",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            await WriteToDeviceAsync();
    }

    void SetProtocol(ProtocolMode mode)
    {
        if (_settings.Protocol == mode) return;
        _settings.Protocol = mode;
        AfterProtocolChanged();
        RefreshDevice();
        SetStatus("Способ записи: " + ProtocolTitle(_settings.EffectiveProtocol) + ". Нажмите «Записать в устройство», чтобы применить.");
    }

    void AfterProtocolChanged()
    {
        _settings.EnsureTriggers();
        RebuildLayerUi();
        UpdateProtocolMenu();
        SelectControl(_pad.Selected);
        _pad.Invalidate();
        UpdateDirty();
        ApplyAgent();
        ScheduleSave();
    }

    void UpdateProtocolMenu()
    {
        foreach (var (mode, item) in _protocolItems) item.Checked = mode == _settings.Protocol;
        _layersMenu.Enabled = _settings.EffectiveProtocol == ProtocolMode.Legacy;
        _layersMenu.ToolTipText = _layersMenu.Enabled ? "" : "Прямой протокол не поддерживает слои";
    }

    void UpdateDirty()
    {
        _settings.EnsureTriggers();
        string signature = ProfileCompiler.Signature(ProfileCompiler.Compile(_settings));
        _dirtyLabel.Text = _settings.LastWrittenSignature == null
            ? "● Настройки ещё не записаны в устройство"
            : "● Есть незаписанные изменения";
        _dirtyLabel.Visible = signature != _settings.LastWrittenSignature;
    }

    async Task WriteToDeviceAsync()
    {
        if (_writing) return;
        RefreshDevice();
        if (_device is not { } device)
        {
            MessageBox.Show(this,
                "Макро-клавиатура не найдена.\n\nПодключите её USB-кабелем (по Bluetooth запись не работает) и попробуйте ещё раз.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.EnsureTriggers();
        var packets = ProfileCompiler.Compile(_settings);
        bool sendInit = ProfileCompiler.SendsInitPacket(_settings.EffectiveProtocol);
        _writing = true;
        _writeButton.Enabled = false;
        UseWaitCursor = true;
        SetStatus("Запись в устройство…");
        try
        {
            var progress = new Progress<int>(p => SetStatus($"Запись в устройство… {p}%"));
            await Task.Run(() =>
            {
                using var pad = MacroPadDevice.Open(device);
                pad.Write(packets, sendInit, progress);
            });
            _settings.LastWrittenSignature = ProfileCompiler.Signature(packets);
            SaveNow();
            SetStatus("✓ Записано. Переподключите кабель клавиатуры и проверьте в «Устройство → Монитор нажатий» (F8).");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка записи: " + ex.Message);
            MessageBox.Show(this, "Не удалось записать настройки в устройство:\n\n" + ex.Message +
                                  "\n\nПереподключите клавиатуру и попробуйте ещё раз.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _agentStatus.Text = "⚠ Служебные клавиши заняты другой программой: " + string.Join(", ", _failedSlots.Select(TriggerKeys.Describe));
            _agentStatus.ForeColor = Theme.Danger;
        }
        else
        {
            _agentStatus.Text = slots.Count == 0 ? "Фоновые действия: не используются" : $"Фоновые действия: {slots.Count}, работают";
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
            _monitor?.LogAgent("ошибка: " + ex.Message);
            if (fromTest || Visible)
                MessageBox.Show(this, ex.Message, "Не удалось выполнить действие", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                _tray.ShowBalloonTip(5000, "Не удалось выполнить действие", ex.Message, ToolTipIcon.Warning);
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
        for (int i = 0; i < layers; i++) _layerBox.Items.Add($"Слой {i + 1}");
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
            SetStatus("Слои переключаются на самом устройстве (обычно отдельной кнопкой или комбинацией) — это зависит от модели.");
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
                ? $"Автозапуск включён: {AppName} будет запускаться в трее вместе с Windows."
                : "Автозапуск выключен.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось изменить автозапуск:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _autostartItem.Checked = Autostart.IsEnabled;
    }

    // ───────────────────────── Профили ─────────────────────────

    void OpenProfile()
    {
        using var dialog = new OpenFileDialog { Title = "Открыть профиль", Filter = "Профиль OshiMakiKey (*.json)|*.json|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _settings.Profile = SettingsStore.LoadProfile(dialog.FileName);
            AfterProfileReplaced();
            SetStatus("Профиль загружен. Нажмите «Записать в устройство», чтобы применить его.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть профиль:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void SaveProfileAs()
    {
        string name = string.Join("_", _settings.Profile.Name.Split(Path.GetInvalidFileNameChars()));
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить профиль",
            Filter = "Профиль OshiMakiKey (*.json)|*.json",
            FileName = (name.Length > 0 ? name : "profile") + ".json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SettingsStore.SaveProfile(_settings.Profile, dialog.FileName);
            SetStatus("Профиль сохранён: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить профиль:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ClearLayer()
    {
        if (MessageBox.Show(this, "Сбросить все действия текущего слоя на «Нет действия»?", AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var id in Enum.GetValues<ControlId>()) CurrentLayer.Actions[id] = new PadAction();
        AfterProfileReplaced();
    }

    void ResetToDefault()
    {
        if (MessageBox.Show(this, "Заменить все настройки примером (копировать, вставить, отменить, музыка, громкость)?", AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _settings.Profile = Profile.CreateDefault();
        AfterProfileReplaced();
    }

    // ───────────────────────── Справка ─────────────────────────

    void ShowHowItWorks() => MessageBox.Show(this,
        "1. Слева выберите клавишу или действие ручки.\n" +
        "2. Справа выберите, что оно должно делать.\n" +
        "3. Нажмите «Записать в устройство».\n" +
        "4. Проверьте результат в «Устройство → Монитор нажатий» (F8).\n\n" +
        "Действия с отметкой ✓ записываются в память самой клавиатуры и работают даже без программы.\n\n" +
        "Действия с отметкой ⚙ клавиатура сама выполнить не может (у новой ревизии CH552 это мультимедиа, мышь, " +
        "последовательности нажатий и все системные действия). Для них устройство отправляет служебную клавишу F13–F24, " +
        $"а {AppName}, работающая в трее, выполняет действие. Включите «Настройки → Запускать вместе с Windows», чтобы это работало всегда.\n\n" +
        "Запись идёт только по USB-кабелю. Драйвер не нужен — используется стандартный драйвер Windows.",
        "Как это работает", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void ShowAbout() => MessageBox.Show(this,
        $"{AppName} {Branding.Version}\n\nНастройка макро-клавиатур на чипе CH552 (USB ID 1189:8890): 6 клавиш и ручка.\n\n" +
        $"Способ записи сейчас: {ProtocolTitle(_settings.EffectiveProtocol)}.\n" +
        "Протоколы: kriomant/ch57x-keyboard-tool и rOzzy1987/MacroPad.\n\n" +
        "Настройки хранятся в: " + SettingsStore.FilePath,
        "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
            SetStatus("Не удалось сохранить настройки: " + ex.Message);
        }
    }
}
