using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Мастер проверки записи. Пишет в плату прямым форматом тестовые буквы — в кнопки 1–6 и во все кнопки,
/// где может оказаться ручка (7–21), у каждой своя буква, — затем просит нажать каждую клавишу и ручку.
/// По пришедшим буквам видно, работает ли запись, и какие номера у поворотов и нажатия ручки на этой плате.
/// Всё, что прислала плата, пишется в журнал.
/// </summary>
internal sealed class WriteCheckPage : UserControl
{
    abstract record Step;
    sealed record PressStep(ControlId Control) : Step;
    sealed record ReplugStep : Step;
    sealed record WriteStep : Step;

    const int FirstProbeSlot = 1;
    const int LastProbeSlot = 30; // 16 клавиш + 3 ручки × 3 действия с запасом: буквы a–z, затем цифры 1–4

    readonly DriverDef _driver;
    readonly ControlId[] KeyIds;
    readonly ControlId[] KnobIds;

    const int QuietMs = 700;

    readonly Label _title = new();
    readonly Label _text = new();
    readonly Label _progress = new();
    readonly Label _device = new();
    readonly Label _received = new();
    readonly ListBox _log = new();
    readonly Button _primary = new();
    readonly Button _skip = new();
    readonly System.Windows.Forms.Timer _quiet = new() { Interval = QuietMs };
    readonly System.Windows.Forms.Timer _replugSettle = new() { Interval = 1500 };
    readonly CappedLog _file = LogFiles.OpenMonitorLog();
    readonly List<Step> _steps = new();
    readonly List<string> _captured = new();
    readonly Dictionary<ControlId, int> _slotSeen = new();
    readonly Dictionary<ControlId, int> _letterCount = new();
    int _index = -1;
    bool _started;
    bool _sawRemoval;
    bool _busy;
    string? _savedPath;

    /// <summary>Подходящий способ записи (null — запись не сработала или мастер закрыт).</summary>
    public ProtocolMode? Result { get; private set; }

    /// <summary>Найденные номера кнопок ручки: влево, нажатие, вправо (null — не найдены).</summary>
    public List<int>? KnobSlots { get; private set; }

    /// <summary>Мастер закончен или закрыт: смотрите <see cref="Result"/> и <see cref="KnobSlots"/>.</summary>
    public event EventHandler? Finished;

    public WriteCheckPage(DriverDef driver)
    {
        _driver = driver;
        KeyIds = driver.Controls.Where(Model.Controls.IsKey).ToArray();
        KnobIds = driver.Controls.Where(c => !Model.Controls.IsKey(c)).ToArray();

        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(16)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _title.AutoSize = true;
        _title.Font = Theme.Title;
        _title.Margin = new Padding(0, 0, 0, Theme.S(4));
        _progress.AutoSize = true;
        _progress.ForeColor = Theme.Muted;
        _progress.Margin = new Padding(0, 0, 0, Theme.S(12));
        foreach (var label in new[] { _text, _device, _received })
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(Theme.S(630), 0);
            label.Margin = new Padding(0, 0, 0, Theme.S(10));
        }
        _text.Font = Theme.Subtitle;
        _device.Font = Theme.Strong;
        _received.ForeColor = Theme.AccentText;

        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 9f);
        _log.IntegralHeight = false;
        _log.HorizontalScrollbar = true;

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(10), 0, 0) };
        StyleButton(_primary, primary: true);
        StyleButton(_skip, primary: false);
        _primary.Click += (_, _) => OnPrimary();
        _skip.Click += (_, _) => OnSkip();
        buttons.Controls.Add(_primary);
        buttons.Controls.Add(_skip);
        // Выход со страницы в любой момент (раньше это был крестик окна).
        var cancel = new Button { Text = L.T("button.cancel", "Отмена") };
        StyleButton(cancel, primary: false);
        cancel.Click += (_, _) => { if (!_busy) Finished?.Invoke(this, EventArgs.Empty); };
        buttons.Controls.Add(cancel);

        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_progress, 0, 1);
        root.Controls.Add(_text, 0, 2);
        root.Controls.Add(_device, 0, 3);
        root.Controls.Add(_received, 0, 4);
        root.Controls.Add(_log, 0, 5);
        root.Controls.Add(buttons, 0, 6);
        Controls.Add(root);

        _quiet.Tick += (_, _) => FinishPress();
        _replugSettle.Tick += (_, _) =>
        {
            _replugSettle.Stop();
            Log(L.T("wizard.log.replugged", "Плата переподключена"));
            Next();
        };

        // Проверяем, что журнал доступен для записи (ошибку покажем строкой в протоколе).
        string? logError = CappedLog.Append(LogFiles.MonitorLog, Array.Empty<string>(), LogFiles.MonitorLogLines);

        _steps.Add(new ReplugStep());
        _steps.Add(new WriteStep());
        foreach (var control in driver.Controls) _steps.Add(new PressStep(control));

        Log(L.F("wizard.log.opened", "──── Проверка записи открыта {0:dd.MM.yyyy HH:mm:ss}, {1} {2}", DateTime.Now, Branding.AppName, Branding.Version));
        if (logError != null)
            Log(L.F("wizard.log.logError", "Не удалось открыть monitor.log: {0} — протокол сохранится отдельным файлом", logError));
        ShowIntro();
        UpdateDeviceStatus();
        ForeColor = Theme.Text;
        Theme.Style(this);
    }

    static void StyleButton(Button button, bool primary)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(Theme.S(120), Theme.S(36));
        button.FlatStyle = FlatStyle.Flat;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(Theme.S(8), 0, 0, 0);
        button.Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0);
        button.BackColor = primary ? Theme.Accent : Theme.Surface;
        button.ForeColor = primary ? Theme.OnAccent : Theme.Text;
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentDark : Theme.Hover;
    }

    // ───────────────────────── Тестовые буквы ─────────────────────────

    /// <summary>Буква для кнопки с номером slot: 1 → a, 2 → b, …, 21 → u.</summary>
    // Коды a–z (0x04–0x1D) и дальше цифры 1–9 (0x1E–0x26) идут подряд, поэтому номер кнопки — просто смещение.
    static byte CodeForSlot(int slot) => (byte)(0x04 + slot - FirstProbeSlot);

    static int? SlotForCode(byte code) =>
        code >= CodeForSlot(FirstProbeSlot) && code <= CodeForSlot(LastProbeSlot) ? code - 0x04 + FirstProbeSlot : null;

    static List<byte[]> ProbePackets()
    {
        var packets = new List<byte[]>();
        for (int slot = FirstProbeSlot; slot <= LastProbeSlot; slot++)
            packets.Add(DirectProtocol.Bind((byte)slot, 0, new[] { CodeForSlot(slot) }));
        packets.Add(DirectProtocol.Close());
        return packets;
    }

    // ───────────────────────── Шаги ─────────────────────────

    void ShowIntro()
    {
        _title.Text = L.T("wizard.title", "Проверка записи");
        _progress.Text = "";
        _text.Text = L.T("wizard.intro", "Мастер запишет в клавиатуру тестовые буквы и попросит по очереди нажать каждую клавишу и ручку. По буквам он поймёт, работает ли запись и какие номера у ручки на вашей плате.\n\nСначала нужно отключить и снова подключить USB-кабель — мастер это увидит сам. Около минуты.");
        _received.Text = "";
        _primary.Text = L.T("wizard.start", "Начать");
        _primary.Visible = true;
        _skip.Visible = false;
    }

    void Next()
    {
        _quiet.Stop();
        _index++;
        if (_index >= _steps.Count)
        {
            Finish();
            return;
        }

        var step = _steps[_index];
        _title.Text = L.T("wizard.title", "Проверка записи");
        _progress.Text = L.F("wizard.step", "Шаг {0} из {1}", _index + 1, _steps.Count);
        _received.Text = "";
        _primary.Visible = false;

        switch (step)
        {
            case PressStep press:
                _captured.Clear();
                _text.Text = PressPrompt(press.Control);
                _skip.Text = L.T("wizard.nothing", "Ничего не происходит");
                _skip.Visible = true;
                break;
            case ReplugStep:
                _sawRemoval = false;
                _text.Text = L.T("wizard.replug", "Отключите USB-кабель клавиатуры и подключите его снова.");
                _skip.Text = L.T("wizard.skip", "Пропустить");
                _skip.Visible = true;
                break;
            case WriteStep:
                _skip.Visible = false;
                _text.Text = L.T("wizard.writing", "Записываю тестовые буквы…");
                RunWrite();
                break;
        }
    }

    static string PressPrompt(ControlId control) => control switch
    {
        ControlId.KnobLeft => L.T("wizard.press.knobLeft", "Поверните ручку ВЛЕВО на один щелчок."),
        ControlId.KnobPress => L.T("wizard.press.knobPress", "НАЖМИТЕ на ручку один раз."),
        ControlId.KnobRight => L.T("wizard.press.knobRight", "Поверните ручку ВПРАВО на один щелчок."),
        _ => L.F("wizard.press.key", "Нажмите клавишу {0} один раз.", UiText.KeyNumber(control)),
    };

    async void RunWrite()
    {
        var device = MacroPadDevice.FindConfigInterface();
        if (device == null)
        {
            Log(L.T("wizard.log.noDevice", "Клавиатура не подключена, запись пропущена"));
            Next();
            return;
        }

        var packets = ProbePackets();
        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                using var pad = MacroPadDevice.Open(device);
                pad.Write(packets, sendInit: false);
            });
            Log(L.F("wizard.log.written", "Записан прямой формат: кнопки {0}–{1} → буквы a–z и цифры 1–4 (кнопка 1 = a, 11 = k, 13 = m, 16 = p, 27 = 1), кадров {2}",
                FirstProbeSlot, LastProbeSlot, packets.Count));
        }
        catch (Exception ex)
        {
            Log(L.F("wizard.log.writeError", "Ошибка записи: {0}", ex.Message));
        }
        finally
        {
            _busy = false;
        }
        Next();
    }

    void FinishPress()
    {
        _quiet.Stop();
        if (_index < 0 || _index >= _steps.Count || _steps[_index] is not PressStep press) return;
        string slot = _slotSeen.TryGetValue(press.Control, out int s)
            ? " " + L.F("wizard.log.slot", "[кнопка прошивки {0}, букв: {1}]", s, _letterCount.GetValueOrDefault(press.Control))
            : "";
        Log($"{UiText.Title(press.Control)} → {(_captured.Count == 0 ? L.T("wizard.log.noEvents", "нет событий") : string.Join("  ", _captured))}{slot}");
        Next();
    }

    void Finish()
    {
        _quiet.Stop();
        _index = _steps.Count;

        bool keysWork = KeyIds.Any(_slotSeen.ContainsKey);
        if (keysWork) Result = ProtocolMode.Direct;
        if (KnobIds.Any(_slotSeen.ContainsKey))
        {
            KnobSlots = KnobIds.Select(id => _slotSeen.TryGetValue(id, out int s) ? s : (int)_driver.SlotOf(id)).ToList();
        }

        string keys = string.Join(", ", KeyIds.Select(id => $"{UiText.KeyNumber(id)}→{(_slotSeen.TryGetValue(id, out int s) ? s.ToString() : "—")}"));
        string knob = string.Join(", ", KnobIds.Select(id => $"{UiText.Glyph(id)}→{(_slotSeen.TryGetValue(id, out int s) ? s.ToString() : "—")}"));
        Log(L.F("wizard.log.summary", "Итог: запись {0}; номера клавиш {1}; номера ручки {2}",
            keysWork ? L.T("wizard.log.works", "работает") : L.T("wizard.log.failed", "не сработала"), keys, knob));
        string saved = SaveProtocol() is string path ? L.F("wizard.saved", "Протокол сохранён: user_data\\{0}.", Path.GetFileName(path)) : "";

        _title.Text = keysWork ? L.T("wizard.works", "Запись работает!") : L.T("wizard.failed", "Запись не сработала");
        _progress.Text = "";
        _text.Text = keysWork
            ? L.F("wizard.works.text", "Клавиши отвечают тестовыми буквами. Номера ручки: {0}.\n\nНажмите «Готово» — программа сохранит этот способ и предложит записать ваши настройки.\n\n{1}", knob, saved)
            : L.F("wizard.failed.text", "Клавиши не прислали тестовых букв. Всё, что прислала клавиатура, записано — этого хватит, чтобы разобраться дальше.\n\n{0}", saved);
        _received.Text = "";
        _primary.Text = keysWork ? L.T("wizard.done", "Готово") : L.T("button.close", "Закрыть");
        _primary.Visible = true;
        _skip.Visible = false;
    }

    void OnPrimary()
    {
        if (!_started)
        {
            _started = true;
            Next();
            return;
        }
        if (_index >= _steps.Count)
        {
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    void OnSkip()
    {
        if (_busy || _index < 0 || _index >= _steps.Count) return;
        if (_steps[_index] is ReplugStep) Log(L.T("wizard.log.replugSkipped", "Переподключение пропущено"));
        if (_steps[_index] is PressStep) FinishPress();
        else Next();
    }

    // ───────────────────────── Ввод с клавиатуры ─────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (RawInput.Register(Handle) is string error) Log(L.F("monitor.registerError", "ОШИБКА подписки на ввод: {0}", error));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }

    void Cleanup()
    {
        RawInput.Unregister();
        _quiet.Dispose();
        _replugSettle.Dispose();
        Log(L.T("wizard.log.closed", "──── Проверка записи закрыта"));
        // Мастер закрыли раньше итога — протокол всё равно сохраняем.
        if (_savedPath == null && _started) SaveProtocol();
        _file.Dispose();
    }

    /// <summary>Окно получило WM_DEVICECHANGE (его шлют только окнам верхнего уровня) — передаёт сюда.</summary>
    public void DeviceChanged()
    {
        if (IsHandleCreated) BeginInvoke(OnDeviceChange);
    }

    /// <summary>Отдельный файл с полным протоколом — на случай, если monitor.log был недоступен.</summary>
    string? SaveProtocol()
    {
        _savedPath = LogFiles.SaveSnapshot("diagnostics", _log.Items.Cast<string>()) ?? _savedPath;
        return _savedPath;
    }

    // Тестовые буквы не должны срабатывать как команды этого окна.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawInput.WM_INPUT && RawInput.Read(m.LParam) is { } input && input.Device != IntPtr.Zero &&
            DriverStore.IsPadDeviceName(RawInput.DeviceName(input.Device)))
        {
            OnPadInput(input);
        }
        base.WndProc(ref m);
    }

    void OnPadInput(RawEvent input)
    {
        string? text = input switch
        {
            RawKeyboardEvent key => $"{key.Key}:{(key.Up ? L.T("monitor.released", "отпущена") : L.T("monitor.pressed", "нажата"))}",
            RawHidEvent hid => L.T("wizard.token.media", "мм") + (hid.Report.Any(b => b != 0) ? $"[{Convert.ToHexString(hid.Report)}]" : "0"),
            RawMouseEvent mouse when mouse.ButtonFlags != 0 =>
                L.T("wizard.token.mouse", "мышь") + $"{mouse.ButtonFlags:X4}{(mouse.WheelDelta != 0 ? $"({mouse.WheelDelta})" : "")}",
            _ => null,
        };
        if (text == null) return;

        if (_index >= 0 && _index < _steps.Count && _steps[_index] is PressStep press)
        {
            _captured.Add(text);
            _received.Text = L.F("wizard.received", "Получено: {0}", string.Join("  ", _captured.TakeLast(12)));
            _quiet.Stop();
            _quiet.Start();
            if (input is RawKeyboardEvent { Up: false } down && SlotForCode(KeyCatalog.FromVirtualKey(down.Key)) is int slot)
            {
                _slotSeen.TryAdd(press.Control, slot);
                _letterCount[press.Control] = _letterCount.GetValueOrDefault(press.Control) + 1;
            }
        }
        else
        {
            Log(L.F("wizard.log.outside", "вне шага: {0}", text));
        }
    }

    void OnDeviceChange()
    {
        UpdateDeviceStatus();
        bool connected = MacroPadDevice.FindConfigInterface() != null;
        if (_index < 0 || _index >= _steps.Count || _steps[_index] is not ReplugStep) return;
        if (!connected && !_sawRemoval)
        {
            _sawRemoval = true;
            Log(L.T("wizard.log.unplugged", "Плата отключена"));
            _text.Text = L.T("wizard.plugBack", "Теперь подключите кабель обратно.");
        }
        else if (connected && _sawRemoval && !_replugSettle.Enabled)
        {
            // Даём Windows закончить установку интерфейсов, прежде чем писать.
            _replugSettle.Start();
        }
    }

    void UpdateDeviceStatus()
    {
        bool connected = MacroPadDevice.FindConfigInterface() != null;
        _device.Text = connected ? L.T("wizard.connected", "● Клавиатура подключена") : L.T("wizard.disconnected", "○ Клавиатура отключена");
        _device.ForeColor = connected ? Theme.Success : Theme.Danger;
    }

    void Log(string text)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {L.T("wizard.log.prefix", "Мастер")}  {text}";
        _log.Items.Add(line);
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        try
        {
            _file.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }
}
