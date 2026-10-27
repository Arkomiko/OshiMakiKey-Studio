using System.Diagnostics;
using System.Drawing.Drawing2D;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// «Монитор нажатий»: показывает, что макро-клавиатура реально отправляет в компьютер — какое сочетание клавиш она
/// эмулирует, какую мультимедиа-команду шлёт и что выполнила программа, — и пишет это в журнал monitor.log.
///
/// Безопасность. Монитор подписан на ввод всех клавиатур (иначе плату не увидеть), поэтому:
///  • клавиши других устройств никогда не называются — только «клавиша нажата» и от какого устройства, и только
///    пока окно монитора активно (ввод в другие программы сюда не попадает);
///  • в журнал на диске пишутся только события самой платы, действия программы и служебные строки —
///    набранный на обычной клавиатуре текст туда не попадает никогда.
/// </summary>
internal sealed class MonitorForm : Form
{
    const int MaxLines = LogFiles.MonitorLogLines; // и в окне, и в monitor.log — не больше 500 событий
    const int RepeatGapMs = 400; // ближе — это повтор удерживаемой кнопки (прошивка повторяет тап каждые ~110 мс)

    enum EntryKind { Key, Release, Media, Mouse, Program, Info, Other, Raw }

    /// <summary>Строка монитора. ToString — текстом (для «Копировать всё» и журнала).</summary>
    sealed record Entry(DateTime Time, EntryKind Kind, string Title, string Detail, bool FromPad)
    {
        public override string ToString() =>
            $"{Time:HH:mm:ss.fff}  {KindName(Kind),-12} {Title}{(Detail.Length > 0 ? "   · " + Detail : "")}";
    }

    readonly ListBox _log = new();
    readonly Label _hint = new();
    readonly CheckBox _showOthers = new();
    readonly string _logPath = LogFiles.MonitorLog;
    readonly List<HidInputReader> _readers = new();
    readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 800 };
    CappedLog? _file;

    // Модификаторы, зажатые платой: из них и следующей клавиши складывается сочетание «Ctrl + C».
    bool _ctrl, _shift, _alt, _win, _keyInChord;
    string _lastTitle = "";
    DateTime _lastTime;

    // Когда плата нажала клавишу или мультимедиа-кнопку — чтобы при отпускании показать, сколько её держали.
    readonly Dictionary<Keys, (DateTime At, string Title)> _keysDown = new();
    (DateTime At, string Title)? _mediaDown;

    /// <summary>По условию на действие — название кнопки текущего слоя, которой оно назначено (null — ничьё).</summary>
    public Func<Func<PadAction, bool>, string?>? FindControl { get; set; }

    public MonitorForm()
    {
        Text = L.T("monitor.title", "Монитор нажатий") + " — " + Branding.AppName;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Theme.S(860), Theme.S(560));
        MinimumSize = new Size(Theme.S(600), Theme.S(380));
        KeyPreview = true;

        _hint.Dock = DockStyle.Top;
        _hint.AutoSize = false;
        _hint.Height = Theme.S(78);
        _hint.Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(6));
        _hint.ForeColor = Theme.Text;
        _hint.Text = L.T("monitor.hint.safe", "Нажимайте клавиши и крутите ручку макро-клавиатуры — здесь видно, какое сочетание или мультимедиа-команду она отправляет. Клавиши других устройств не называются и в журнал не пишутся. Журнал: user_data\\monitor.log.");

        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.IntegralHeight = false;
        _log.DrawMode = DrawMode.OwnerDrawFixed;
        _log.ItemHeight = Theme.S(34);
        _log.DrawItem += DrawEntry;

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

        _showOthers.Text = L.T("monitor.showOthers.masked", "Показывать другие устройства (без названий клавиш)");
        _showOthers.AutoSize = true;
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
            Info(L.T("monitor.usbChanged", "USB: состав устройств изменился"));
            StartReaders();
        };

        string? logError = CappedLog.Append(_logPath, Array.Empty<string>(), LogFiles.MonitorLogLines);
        _file = LogFiles.OpenMonitorLog();
        Info(L.F("monitor.opened", "──── Монитор открыт {0:dd.MM.yyyy HH:mm:ss}, {1} {2}", DateTime.Now, Branding.AppName, Branding.Version));
        if (logError != null) Info(L.F("monitor.logError", "Не удалось открыть monitor.log: {0}", logError));
        Theme.ApplyTo(this);
        _log.BackColor = Theme.Surface; // строки рисуются сами, фон списка — как у окна
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

    /// <summary>Действие, выполненное программой (фоновым агентом): что сделано и для какой кнопки.</summary>
    public void LogAgent(string title, string detail = "") => Append(new Entry(DateTime.Now, EntryKind.Program, title, detail, true));

    /// <summary>Для --monitor-snapshot: примерные строки, чтобы проверить вид списка без нажатий.</summary>
    internal void AddSamples()
    {
        AddPadEntry(EntryKind.Key, "Ctrl + C", null, UiText.Title(ControlId.Key1));
        AddPadEntry(EntryKind.Key, "F16", null, L.T("monitor.serviceKey.detail", "служебная клавиша для программы"));
        LogAgent(MediaCatalog.NameOf(0xCD), "Клавиша 5 · F16");
        Append(new Entry(DateTime.Now, EntryKind.Release, "Ctrl + C", L.F("monitor.held", "держали {0:0.00} с", 0.12), true));
        AddPadEntry(EntryKind.Media, MediaCatalog.NameOf(0xEA), null, "Ручка: поворот влево");
        AddPadEntry(EntryKind.Media, MediaCatalog.NameOf(0xEA), null, "Ручка: поворот влево");
        Append(new Entry(DateTime.Now, EntryKind.Raw, "03EA00", L.T("monitor.media", "Мультимедиа"), true));
        Append(new Entry(DateTime.Now, EntryKind.Other, L.T("monitor.hiddenKey", "Клавиша нажата (скрыта)"), "046D:C52B", false));
    }

    void Info(string text) => Append(new Entry(DateTime.Now, EntryKind.Info, text.TrimStart('★', ' '), "", true));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (RawInput.Register(Handle) is string error) Info(L.F("monitor.registerError", "ОШИБКА подписки на ввод: {0}", error));
        int padCollections = RawInput.DeviceNames().Count(DriverStore.IsPadDeviceName);
        Info(L.F("monitor.collections", "★ Raw Input видит коллекций макро-клавиатуры: {0}", padCollections));
        StartReaders();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RawInput.Unregister();
        StopReaders();
        _deviceTimer.Dispose();
        Info(L.T("monitor.closed", "──── Монитор закрыт"));
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
            Info(L.T("monitor.notConnected", "★ Макро-клавиатура не подключена"));
            return;
        }
        Info(L.F("monitor.connected", "★ Макро-клавиатура подключена: интерфейсов {0}, драйвер — {1}",
            interfaces.Count, MacroPadDevice.DetectDriver()?.Title ?? "—"));
        foreach (var info in interfaces.Where(i => i.UsagePage == 0x0C || (i.UsagePage == 0x01 && i.Usage == 0x00)))
        {
            try
            {
                var reader = new HidInputReader(info);
                reader.ReportReceived += (r, report) => BeginInvokeSafe(() =>
                    Append(new Entry(DateTime.Now, EntryKind.Raw, Convert.ToHexString(report), InterfaceName(r.Info), true)));
                reader.Start();
                _readers.Add(reader);
            }
            catch (IOException ex)
            {
                Info($"{InterfaceName(info)}: {ex.Message}");
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
            // wParam = 1 (RIM_INPUTSINK): ввод пришёл, когда активно другое окно.
            bool background = m.WParam == (IntPtr)1;
            if (DriverStore.IsPadDeviceName(name)) OnPadInput(input);
            else if (_showOthers.Checked && !background) OnOtherInput(input, name);
        }
        else if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            _deviceTimer.Stop();
            _deviceTimer.Start();
        }
        base.WndProc(ref m);
    }

    // ───────────────────────── События платы ─────────────────────────

    void OnPadInput(RawEvent input)
    {
        switch (input)
        {
            case RawKeyboardEvent key:
                OnPadKey(key);
                break;
            case RawHidEvent { Usages.Length: > 0 } hid when hid.UsagePage == 0x0C:
                foreach (var usage in hid.Usages)
                    AddPadEntry(EntryKind.Media, MediaCatalog.NameOf(usage), a => a.Kind == ActionKind.Media && a.MediaCode == usage);
                _mediaDown = (DateTime.Now, string.Join(" + ", hid.Usages.Select(MediaCatalog.NameOf)));
                break;
            case RawHidEvent { Usages.Length: 0 } hid when hid.UsagePage == 0x0C && _mediaDown is { } media:
                // Пустой отчёт мультимедиа — кнопку отпустили.
                Released(media.Title, media.At);
                _mediaDown = null;
                break;
            case RawHidEvent { Usages.Length: > 0 } hid:
                AddPadEntry(EntryKind.Media, L.T("monitor.system", "Система") + " " + string.Join(" + ", hid.Usages.Select(u => $"0x{u:X2}")), null);
                break;
            case RawMouseEvent mouse when DescribeMouse(mouse) is string text:
                AddPadEntry(EntryKind.Mouse, text, null);
                break;
        }
    }

    /// <summary>Складывает нажатия платы в сочетание: модификаторы копятся, клавиша завершает «Ctrl + C».</summary>
    void OnPadKey(RawKeyboardEvent key)
    {
        bool down = !key.Up;
        switch (key.Key)
        {
            case Keys.ControlKey or Keys.LControlKey or Keys.RControlKey: Modifier(ref _ctrl, down); return;
            case Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey: Modifier(ref _shift, down); return;
            case Keys.Menu or Keys.LMenu or Keys.RMenu: Modifier(ref _alt, down); return;
            case Keys.LWin or Keys.RWin: Modifier(ref _win, down); return;
        }
        if (!down)
        {
            if (_keysDown.Remove(key.Key, out var pressed)) Released(pressed.Title, pressed.At);
            return;
        }
        _keyInChord = true;

        if (key.Key is >= Keys.F13 and <= Keys.F24)
        {
            // Служебная клавиша: её ловит программа и выполняет действие (оно появится строкой «Программа»).
            AddPadEntry(EntryKind.Key, ChordName(key.Key.ToString()), null, L.T("monitor.serviceKey.detail", "служебная клавиша для программы"));
            _keysDown[key.Key] = (DateTime.Now, ChordName(key.Key.ToString()));
            return;
        }
        var chord = new KeyChord { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win, Key = KeyCatalog.FromVirtualKey(key.Key) };
        string title = chord.Key != 0 ? BindingText.Chord(chord) : ChordName(key.Key.ToString());
        AddPadEntry(EntryKind.Key, title, chord.Key == 0 ? null : a => a.Kind == ActionKind.Keys && a.Chords.Any(c => SameChord(c, chord)));
        _keysDown[key.Key] = (DateTime.Now, title);
    }

    /// <summary>Строка «отпущена»: что отпустили и сколько держали.</summary>
    void Released(string title, DateTime at)
    {
        double seconds = (DateTime.Now - at).TotalSeconds;
        Append(new Entry(DateTime.Now, EntryKind.Release, title, L.F("monitor.held", "держали {0:0.00} с", seconds), true));
    }

    void Modifier(ref bool state, bool down)
    {
        if (down)
        {
            if (!_ctrl && !_shift && !_alt && !_win) _keyInChord = false;
            state = true;
            return;
        }
        // Отпустили модификатор, не нажав ничего вместе с ним — плата отправила одиночный модификатор.
        if (state && !_keyInChord)
        {
            var chord = new KeyChord { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win };
            AddPadEntry(EntryKind.Key, BindingText.Chord(chord), a => a.Kind == ActionKind.Keys && a.Chords.Any(c => SameChord(c, chord)));
            _keyInChord = true;
        }
        state = false;
    }

    string ChordName(string key)
    {
        var parts = new List<string>();
        if (_ctrl) parts.Add("Ctrl");
        if (_shift) parts.Add("Shift");
        if (_alt) parts.Add("Alt");
        if (_win) parts.Add("Win");
        parts.Add(key);
        return string.Join(" + ", parts);
    }

    static bool SameChord(KeyChord a, KeyChord b) => a.Key == b.Key && a.ModifierMask == b.ModifierMask;

    /// <summary>Строка события платы: какая кнопка его отправила (если действие есть в пресете) и не повтор ли это.</summary>
    void AddPadEntry(EntryKind kind, string title, Func<PadAction, bool>? match, string? detail = null)
    {
        var now = DateTime.Now;
        bool repeat = title == _lastTitle && (now - _lastTime).TotalMilliseconds < RepeatGapMs;
        _lastTitle = title;
        _lastTime = now;
        var parts = new List<string>();
        if (match != null && FindControl?.Invoke(match) is string control) parts.Add(control);
        if (detail != null) parts.Add(detail);
        if (repeat) parts.Add(L.T("monitor.repeat", "повтор — кнопку держат"));
        Append(new Entry(now, kind, title, string.Join(" · ", parts), true));
    }

    // ───────────────────────── Другие устройства (без подробностей) ─────────────────────────

    void OnOtherInput(RawEvent input, string deviceName)
    {
        string source = SourceName(deviceName);
        switch (input)
        {
            case RawKeyboardEvent { Up: false }:
                // Какая именно клавиша — не показываем: так в монитор не попадёт набранный текст или пароль.
                Append(new Entry(DateTime.Now, EntryKind.Other, L.T("monitor.hiddenKey", "Клавиша нажата (скрыта)"), source, false));
                break;
            case RawMouseEvent mouse when DescribeMouse(mouse) is string text:
                Append(new Entry(DateTime.Now, EntryKind.Mouse, text, source, false));
                break;
            case RawHidEvent { Usages.Length: > 0 } hid when hid.UsagePage == 0x0C:
                Append(new Entry(DateTime.Now, EntryKind.Media, string.Join(" + ", hid.Usages.Select(MediaCatalog.NameOf)), source, false));
                break;
        }
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

    static string? DescribeMouse(RawMouseEvent mouse)
    {
        var parts = new List<string>();
        if ((mouse.ButtonFlags & 0x0001) != 0) parts.Add(L.T("monitor.leftDown", "левая нажата"));
        if ((mouse.ButtonFlags & 0x0004) != 0) parts.Add(L.T("monitor.rightDown", "правая нажата"));
        if ((mouse.ButtonFlags & 0x0010) != 0) parts.Add(L.T("monitor.middleDown", "средняя нажата"));
        if ((mouse.ButtonFlags & 0x0400) != 0)
            parts.Add(mouse.WheelDelta > 0
                ? L.F("monitor.wheelUp", "колесо вверх ({0})", mouse.WheelDelta)
                : L.F("monitor.wheelDown", "колесо вниз ({0})", mouse.WheelDelta));
        // Движение и отпускание кнопок не показываем — иначе журнал утонет в них.
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    // ───────────────────────── Список и журнал ─────────────────────────

    static string KindName(EntryKind kind) => kind switch
    {
        EntryKind.Key => L.T("monitor.kind.key", "Клавиша"),
        EntryKind.Release => L.T("monitor.kind.release", "Отпущена"),
        EntryKind.Media => L.T("monitor.media", "Мультимедиа"),
        EntryKind.Mouse => L.T("monitor.mouse", "Мышь"),
        EntryKind.Program => L.T("monitor.program", "Программа"),
        EntryKind.Raw => L.T("monitor.kind.raw", "Отчёт"),
        EntryKind.Other => L.T("monitor.kind.other", "Другое"),
        _ => "",
    };

    void Append(Entry entry)
    {
        _log.BeginUpdate();
        _log.Items.Add(entry);
        while (_log.Items.Count > MaxLines) _log.Items.RemoveAt(0);
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        _log.EndUpdate();

        // На диск — только плата, программа и служебные строки; сырые отчёты и чужие устройства не пишем.
        if (!entry.FromPad || entry.Kind == EntryKind.Raw) return;
        _file?.WriteLine(entry.ToString());
    }

    /// <summary>Строка: время, цветная метка вида события, сочетание клавишами-«колпачками» или текст, пояснение.</summary>
    void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _log.Items[e.Index] is not Entry entry) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Theme.Scale;
        var bounds = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface)) g.FillRectangle(back, bounds);

        bool faint = !entry.FromPad || entry.Kind == EntryKind.Raw;
        int x = bounds.X + Theme.S(12);
        TextRenderer.DrawText(g, entry.Time.ToString("HH:mm:ss.fff"), Theme.Small, new Rectangle(x, bounds.Y, Theme.S(90), bounds.Height),
            Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        x += Theme.S(96);

        if (entry.Kind == EntryKind.Info)
        {
            TextRenderer.DrawText(g, entry.Title, Theme.Small, new Rectangle(x, bounds.Y, bounds.Right - x, bounds.Height), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            return;
        }

        // Метка вида события.
        var (badgeBack, badgeFore) = entry.Kind switch
        {
            EntryKind.Key => (Theme.AccentSoft, Theme.AccentText),
            EntryKind.Media => (Theme.SuccessSoft, Theme.SuccessText),
            EntryKind.Program => (Theme.WarningSoft, Theme.WarningText),
            _ => (Theme.Badge, Theme.BadgeText),
        };
        string kind = KindName(entry.Kind);
        int badgeWidth = Theme.S(96);
        var badge = new RectangleF(x, bounds.Y + (bounds.Height - 22 * s) / 2, badgeWidth, 22 * s);
        using (var path = Theme.Rounded(badge, 11 * s))
        using (var brush = new SolidBrush(faint ? Theme.Hover : badgeBack))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, kind, Theme.Small, Rectangle.Round(badge), faint ? Theme.Muted : badgeFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        x += badgeWidth + Theme.S(12);

        if (entry.Kind is EntryKind.Key or EntryKind.Release && entry.FromPad)
            x = DrawKeycaps(g, entry.Title, x, bounds);
        else
        {
            var font = faint ? Theme.Small : Theme.Strong;
            var size = TextRenderer.MeasureText(g, entry.Title, font, new Size(int.MaxValue, bounds.Height), TextFormatFlags.NoPrefix);
            int width = Math.Min(size.Width, Math.Max(0, bounds.Right - x - Theme.S(12)));
            TextRenderer.DrawText(g, entry.Title, font, new Rectangle(x, bounds.Y, width, bounds.Height), faint ? Theme.Muted : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            x += width;
        }

        if (entry.Detail.Length > 0)
        {
            x += Theme.S(12);
            TextRenderer.DrawText(g, entry.Detail, Theme.Small, new Rectangle(x, bounds.Y, Math.Max(0, bounds.Right - x - Theme.S(8)), bounds.Height),
                Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Рисует «Ctrl + C» как клавиши-колпачки; возвращает x после последнего.</summary>
    static int DrawKeycaps(Graphics g, string chord, int x, Rectangle bounds)
    {
        float s = Theme.Scale;
        var keys = chord.Split(" + ");
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                TextRenderer.DrawText(g, "+", Theme.Small, new Rectangle(x, bounds.Y, Theme.S(14), bounds.Height), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += Theme.S(14);
            }
            int width = Math.Max(Theme.S(26), TextRenderer.MeasureText(g, keys[i], Theme.Strong).Width + Theme.S(10));
            var cap = new RectangleF(x, bounds.Y + (bounds.Height - 24 * s) / 2, width, 24 * s);
            using (var shadow = Theme.Rounded(new RectangleF(cap.X, cap.Y + 2 * s, cap.Width, cap.Height), 5 * s))
            using (var brush = new SolidBrush(Theme.Border))
                g.FillPath(brush, shadow);
            using (var path = Theme.Rounded(cap, 5 * s))
            using (var brush = new SolidBrush(Theme.Window))
            using (var pen = new Pen(Theme.Border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, keys[i], Theme.Strong, Rectangle.Round(cap), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += width;
        }
        return x;
    }

    void CopyAll()
    {
        var text = string.Join(Environment.NewLine, _log.Items.Cast<object>().Select(x => x.ToString()));
        if (text.Length > 0) Clipboard.SetText(text);
    }

    void OpenLogFile()
    {
        if (File.Exists(_logPath)) Process.Start(new ProcessStartInfo(_logPath) { UseShellExecute = true })?.Dispose();
    }
}
