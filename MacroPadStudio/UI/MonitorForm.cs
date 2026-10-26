using System.Diagnostics;
using System.Text;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// «Монитор нажатий»: показывает, что макро-клавиатура реально отправляет в компьютер (клавиши, мультимедиа,
/// мышь, сырые отчёты её интерфейсов), и пишет это в журнал monitor.log. События других устройств тоже видны —
/// по ним легко убедиться, что сам монитор работает.
/// </summary>
internal sealed class MonitorForm : Form
{
    const int MaxLines = 800;

    readonly ListBox _log = new();
    readonly Label _hint = new();
    readonly CheckBox _showOthers = new();
    readonly string _logPath = LogFiles.MonitorLog;
    readonly string? _selfTestReport;
    readonly List<HidInputReader> _readers = new();
    readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 800 };
    StreamWriter? _file;

    public MonitorForm(string? selfTestReport = null)
    {
        _selfTestReport = selfTestReport;

        Text = L.T("monitor.title", "Монитор нажатий") + " — " + Branding.AppName;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Theme.S(820), Theme.S(540));
        MinimumSize = new Size(Theme.S(560), Theme.S(380));
        KeyPreview = true;

        _hint.Dock = DockStyle.Top;
        _hint.AutoSize = false;
        _hint.Height = Theme.S(78);
        _hint.Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(6));
        _hint.ForeColor = Theme.Text;
        _hint.Text = L.T("monitor.hint", "Нажимайте клавиши и крутите ручку макро-клавиатуры — её события отмечены ★. Нажмите и клавишу на обычной клавиатуре: если она появилась здесь, монитор работает. Журнал сохраняется в user_data\\monitor.log.");

        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 10f);
        _log.BorderStyle = BorderStyle.None;
        _log.IntegralHeight = false;
        _log.HorizontalScrollbar = true;

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(52),
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(12), Theme.S(8)),
            BackColor = Theme.Window,
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _showOthers.Text = L.T("monitor.showOthers", "Показывать и другие устройства");
        _showOthers.AutoSize = true;
        _showOthers.Checked = true;
        _showOthers.Anchor = AnchorStyles.Left;

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(MakeButton(L.T("monitor.clear", "Очистить"), (_, _) => _log.Items.Clear()));
        buttons.Controls.Add(MakeButton(L.T("monitor.copy", "Копировать всё"), (_, _) => CopyAll()));
        buttons.Controls.Add(MakeButton(L.T("monitor.openLog", "Открыть журнал"), (_, _) => OpenLogFile()));
        buttons.Controls.Add(MakeButton(L.T("button.close", "Закрыть"), (_, _) => Close()));
        bottom.Controls.Add(_showOthers, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        Controls.Add(_log);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(_hint);
        Controls.Add(bottom);

        _deviceTimer.Tick += (_, _) =>
        {
            _deviceTimer.Stop();
            Append(L.T("monitor.usbChanged", "USB: состав устройств изменился"));
            StartReaders();
        };

        _file = LogFiles.OpenAppend(_logPath, out string? logError);
        Append(L.F("monitor.opened", "──── Монитор открыт {0:dd.MM.yyyy HH:mm:ss}, {1} {2}", DateTime.Now, Branding.AppName, Branding.Version));
        if (logError != null) Append(L.F("monitor.logError", "Не удалось открыть monitor.log: {0}", logError));
        Theme.ApplyTo(this);
    }

    static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Padding = new Padding(Theme.S(8), Theme.S(2), Theme.S(8), Theme.S(2)),
            Margin = new Padding(Theme.S(8), 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Theme.Hover;
        button.Click += onClick;
        return button;
    }

    /// <summary>Записи от самой программы (например, что фоновый агент выполнил действие).</summary>
    public void LogAgent(string text) => Append($"{L.T("monitor.program", "Программа"),-13} {text}");

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (RawInput.Register(Handle) is string error) Append(L.F("monitor.registerError", "ОШИБКА подписки на ввод: {0}", error));
        int padCollections = RawInput.DeviceNames().Count(DriverStore.IsPadDeviceName);
        Append(L.F("monitor.collections", "★ Raw Input видит коллекций макро-клавиатуры: {0}", padCollections));
        StartReaders();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_selfTestReport == null) return;

        // Режим --monitortest: программно нажать F24 и убедиться, что событие дошло до монитора.
        await Task.Delay(300);
        InputSender.TapKey(Keys.F24);
        await Task.Delay(600);
        File.WriteAllLines(_selfTestReport, _log.Items.Cast<string>());
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RawInput.Unregister();
        StopReaders();
        _deviceTimer.Dispose();
        Append(L.T("monitor.closed", "──── Монитор закрыт"));
        _file?.Dispose();
        _file = null;
        base.OnFormClosed(e);
    }

    /// <summary>Прямое чтение интерфейсов платы, которые Windows не держит монопольно (служебный и мультимедиа).</summary>
    void StartReaders()
    {
        StopReaders();
        var interfaces = MacroPadDevice.Supported.SelectMany(d => HidEnumerator.Enumerate(d.Vid, d.Pid)).ToList();
        if (interfaces.Count == 0)
        {
            Append(L.T("monitor.notConnected", "★ Макро-клавиатура не подключена"));
            return;
        }
        Append(L.F("monitor.connected", "★ Макро-клавиатура подключена: интерфейсов {0}, драйвер — {1}",
            interfaces.Count, MacroPadDevice.DetectDriver()?.Title ?? "—"));
        foreach (var info in interfaces.Where(i => i.UsagePage == 0x0C || (i.UsagePage == 0x01 && i.Usage == 0x00)))
        {
            try
            {
                var reader = new HidInputReader(info);
                reader.ReportReceived += (r, report) => BeginInvokeSafe(() =>
                    Append(L.F("monitor.rawReport", "★ {0,-12} сырой отчёт [{1}]", InterfaceName(r.Info), Convert.ToHexString(report))));
                reader.Start();
                _readers.Add(reader);
            }
            catch (IOException ex)
            {
                Append($"★ {InterfaceName(info)}: {ex.Message}");
            }
        }
    }

    void StopReaders()
    {
        foreach (var reader in _readers) reader.Dispose();
        _readers.Clear();
    }

    static string InterfaceName(HidDeviceInfo info) =>
        info.UsagePage == 0x0C ? L.T("monitor.media", "Мультимедиа") : L.T("monitor.service", "Служебный");

    void BeginInvokeSafe(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    // Пока монитор открыт, нажатия с макро-клавиатуры не должны запускать команды этого окна.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawInput.WM_INPUT && RawInput.Read(m.LParam) is { } input)
        {
            string name = input.Device == IntPtr.Zero ? "" : RawInput.DeviceName(input.Device);
            bool fromPad = DriverStore.IsPadDeviceName(name);
            if ((fromPad || _showOthers.Checked) && Describe(input) is string text)
                Append((fromPad ? "★ " : "  ") + text + (fromPad ? "" : "   · " + SourceName(name)));
        }
        else if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            _deviceTimer.Stop();
            _deviceTimer.Start();
        }
        base.WndProc(ref m);
    }

    /// <summary>«046D:C52B» из имени устройства, «программно» для ввода, созданного программами.</summary>
    static string SourceName(string deviceName)
    {
        if (deviceName.Length == 0) return L.T("monitor.software", "программно");
        int vid = deviceName.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        int pid = deviceName.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        return vid >= 0 && pid >= 0 && vid + 8 <= deviceName.Length && pid + 8 <= deviceName.Length
            ? $"{deviceName.Substring(vid + 4, 4)}:{deviceName.Substring(pid + 4, 4)}".ToUpperInvariant()
            : L.T("monitor.otherDevice", "другое устройство");
    }

    static string? Describe(RawEvent input) => input switch
    {
        RawKeyboardEvent key => L.F("monitor.key", "Клавиатура   {0,-14} {1,-8}   скан-код {2}{3:X2}", KeyName(key.Key),
            key.Up ? L.T("monitor.released", "отпущена") : L.T("monitor.pressed", "нажата"), key.Extended ? "E0 " : "", key.MakeCode),
        RawMouseEvent mouse => DescribeMouse(mouse),
        RawHidEvent hid => $"{(hid.UsagePage == 0x01 ? L.T("monitor.system", "Система") : L.T("monitor.media", "Мультимедиа")),-11}  {DescribeUsages(hid)}   [{Convert.ToHexString(hid.Report)}]",
        _ => null,
    };

    static string KeyName(Keys key) => key switch
    {
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Ctrl",
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
        Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
        Keys.LWin or Keys.RWin => "Win",
        >= Keys.F13 and <= Keys.F24 => key + " " + L.T("monitor.serviceKey", "(служ.)"),
        >= Keys.D0 and <= Keys.D9 => ((char)key).ToString(),
        _ => key.ToString(),
    };

    static string DescribeUsages(RawHidEvent hid)
    {
        if (hid.Usages.Length == 0) return L.T("monitor.releasedAll", "отпущено");
        return string.Join(" + ", hid.Usages.Select(u => hid.UsagePage == 0x0C ? MediaCatalog.NameOf(u) : $"0x{u:X2}"));
    }

    static string? DescribeMouse(RawMouseEvent mouse)
    {
        var parts = new List<string>();
        if ((mouse.ButtonFlags & 0x0001) != 0) parts.Add(L.T("monitor.leftDown", "левая нажата"));
        if ((mouse.ButtonFlags & 0x0002) != 0) parts.Add(L.T("monitor.leftUp", "левая отпущена"));
        if ((mouse.ButtonFlags & 0x0004) != 0) parts.Add(L.T("monitor.rightDown", "правая нажата"));
        if ((mouse.ButtonFlags & 0x0008) != 0) parts.Add(L.T("monitor.rightUp", "правая отпущена"));
        if ((mouse.ButtonFlags & 0x0010) != 0) parts.Add(L.T("monitor.middleDown", "средняя нажата"));
        if ((mouse.ButtonFlags & 0x0020) != 0) parts.Add(L.T("monitor.middleUp", "средняя отпущена"));
        if ((mouse.ButtonFlags & 0x0400) != 0)
            parts.Add(mouse.WheelDelta > 0
                ? L.F("monitor.wheelUp", "колесо вверх ({0})", mouse.WheelDelta)
                : L.F("monitor.wheelDown", "колесо вниз ({0})", mouse.WheelDelta));
        // Простое движение обычной мыши не показываем — иначе журнал утонет в нём.
        return parts.Count == 0 ? null : $"{L.T("monitor.mouse", "Мышь"),-12} " + string.Join(", ", parts);
    }

    void Append(string text)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
        _log.BeginUpdate();
        _log.Items.Add(line);
        while (_log.Items.Count > MaxLines) _log.Items.RemoveAt(0);
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        _log.EndUpdate();
        try
        {
            _file?.WriteLine(line);
        }
        catch (IOException)
        {
            _file = null;
        }
    }

    void CopyAll()
    {
        var text = string.Join(Environment.NewLine, _log.Items.Cast<string>());
        if (text.Length > 0) Clipboard.SetText(text);
    }

    void OpenLogFile()
    {
        if (File.Exists(_logPath)) Process.Start(new ProcessStartInfo(_logPath) { UseShellExecute = true })?.Dispose();
    }
}
