using System.Text;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Мастер подбора способа записи с пошаговой диагностикой. Платы с одним USB ID 1189:8890 прошиты по-разному,
/// и формат можно узнать только опытом. Мастер просит нажимать каждый элемент по очереди и записывает в журнал,
/// что именно он прислал: сначала в исходном состоянии, затем после каждого варианта записи (перед записью —
/// переподключение кабеля). У каждого варианта свои тестовые буквы, поэтому по ним видно, чья запись сработала.
/// </summary>
internal sealed class ProtocolWizardForm : Form
{
    sealed record Variant(ProtocolMode Mode, string Title, string KeyLetters, string KnobLetters);

    abstract record Step(int Variant);
    sealed record PressStep(int Variant, ControlId Control) : Step(Variant);
    sealed record ReplugStep(int Variant) : Step(Variant);
    sealed record WriteStep(int Variant) : Step(Variant);

    // Буквы у вариантов не пересекаются: по одной букве видно, чья запись сработала.
    static readonly Variant[] Variants =
    {
        new(ProtocolMode.Direct, "прямой (формат padclaude)", "abcdef", "ghi"),
        new(ProtocolMode.Vendor, "как у официальной программы", "jklmno", "pqr"),
        new(ProtocolMode.Legacy, "формат ch57x-keyboard-tool", "stuvwx", "yz1"),
    };

    static readonly ControlId[] KeyIds = { ControlId.Key1, ControlId.Key2, ControlId.Key3, ControlId.Key4, ControlId.Key5, ControlId.Key6 };
    static readonly ControlId[] KnobIds = { ControlId.KnobLeft, ControlId.KnobPress, ControlId.KnobRight };
    static readonly ControlId[] AllControls = KeyIds.Concat(KnobIds).ToArray();

    const string DeviceTag = "VID_1189&PID_8890";
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
    readonly StreamWriter? _file;
    readonly List<Step> _steps = new();
    readonly List<string> _captured = new();
    readonly int[] _keyHits = new int[Variants.Length];
    readonly HashSet<ControlId> _knobHits = new();
    int _index = -1;
    bool _started;
    bool _sawRemoval;
    bool _busy;

    /// <summary>Подобранный способ записи (null — не найден или мастер закрыт).</summary>
    public ProtocolMode? Result { get; private set; }

    public ProtocolWizardForm()
    {
        Text = "Подбор способа записи — " + Branding.AppName;
        Icon = Theme.AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(Theme.S(680), Theme.S(600));
        KeyPreview = true;

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
        _received.ForeColor = Theme.AccentDark;

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
            Log("Плата переподключена");
            Next();
        };

        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            _file = new StreamWriter(Path.Combine(SettingsStore.Folder, "monitor.log"), append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (IOException)
        {
            _file = null;
        }

        BuildSteps();
        Log($"──── Диагностика открыта {DateTime.Now:dd.MM.yyyy HH:mm:ss}, {Branding.AppName} {Branding.Version}");
        ShowIntro();
        UpdateDeviceStatus();
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
        button.ForeColor = primary ? Color.White : Theme.Text;
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentDark : Theme.Hover;
    }

    void BuildSteps()
    {
        // Исходное состояние, затем для каждого варианта: переподключить → записать → нажать всё по очереди.
        foreach (var control in AllControls) _steps.Add(new PressStep(-1, control));
        for (int v = 0; v < Variants.Length; v++)
        {
            _steps.Add(new ReplugStep(v));
            _steps.Add(new WriteStep(v));
            foreach (var control in AllControls) _steps.Add(new PressStep(v, control));
        }
    }

    // ───────────────────────── Шаги ─────────────────────────

    void ShowIntro()
    {
        _title.Text = "Подбор способа записи";
        _progress.Text = "";
        _text.Text = "Мастер попросит нажимать каждую клавишу и ручку по очереди и запишет, что они присылают: " +
                     "сначала как есть, потом после каждого из трёх вариантов записи. Перед каждой записью нужно " +
                     "отключить и снова подключить USB-кабель клавиатуры — мастер это увидит сам.\n\n" +
                     "Это займёт около трёх минут. Клавиши во время проверки могут печатать буквы — это нормально.";
        _received.Text = "";
        _primary.Text = "Начать";
        _primary.Visible = true;
        _skip.Visible = false;
    }

    void Next()
    {
        _quiet.Stop();
        _index++;

        // Вариант сработал — нажатия по нему уже собраны, дальше проверять незачем.
        if (_index < _steps.Count && _steps[_index] is ReplugStep && Result != null)
        {
            Finish();
            return;
        }
        if (_index >= _steps.Count)
        {
            Finish();
            return;
        }

        var step = _steps[_index];
        string stage = step.Variant < 0 ? "Исходное состояние" : $"Вариант {step.Variant + 1} из {Variants.Length}: {Variants[step.Variant].Title}";
        _title.Text = stage;
        _progress.Text = $"Шаг {_index + 1} из {_steps.Count}";
        _received.Text = "";
        _primary.Visible = false;

        switch (step)
        {
            case PressStep press:
                _captured.Clear();
                _text.Text = PressPrompt(press.Control);
                _skip.Text = "Ничего не происходит";
                _skip.Visible = true;
                break;
            case ReplugStep:
                _sawRemoval = false;
                _text.Text = "Отключите USB-кабель клавиатуры и подключите его снова.";
                _skip.Text = "Пропустить";
                _skip.Visible = true;
                break;
            case WriteStep write:
                _skip.Visible = false;
                _text.Text = "Записываю тестовые буквы…";
                RunWrite(write.Variant);
                break;
        }
    }

    static string PressPrompt(ControlId control) => control switch
    {
        ControlId.KnobLeft => "Поверните ручку ВЛЕВО на один щелчок.",
        ControlId.KnobPress => "НАЖМИТЕ на ручку.",
        ControlId.KnobRight => "Поверните ручку ВПРАВО на один щелчок.",
        _ => $"Нажмите клавишу {UiText.KeyNumber(control)}.",
    };

    async void RunWrite(int variantIndex)
    {
        var variant = Variants[variantIndex];
        var device = MacroPadDevice.FindConfigInterface();
        if (device == null)
        {
            Log($"Вариант {variantIndex + 1}: клавиатура не подключена, запись пропущена");
            Next();
            return;
        }

        var packets = ProfileCompiler.Compile(TestSettings(variant));
        bool sendInit = ProfileCompiler.SendsInitPacket(variant.Mode);
        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                using var pad = MacroPadDevice.Open(device);
                pad.Write(packets, sendInit);
            });
            Log($"Записан вариант {variantIndex + 1} ({variant.Mode}): клавиши 1–6 → {variant.KeyLetters}, ручка ↺●↻ → {variant.KnobLetters}, кадров {packets.Count}");
        }
        catch (Exception ex)
        {
            Log($"Ошибка записи варианта {variantIndex + 1}: {ex.Message}");
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
        string label = press.Variant < 0 ? "исходно" : $"вариант {press.Variant + 1}";
        Log($"[{label}] {UiText.Title(press.Control)} → {(_captured.Count == 0 ? "нет событий" : string.Join("  ", _captured))}");
        Next();
    }

    void Finish()
    {
        _quiet.Stop();
        _index = _steps.Count;
        _title.Text = Result != null ? "Нашёлся рабочий способ!" : "Подходящий вариант не найден";
        _progress.Text = "";
        string hits = string.Join(", ", Variants.Select((v, i) => $"{v.Mode}: {_keyHits[i]}"));
        string knob = _knobHits.Count == 0 ? "ручка буквы не прислала" : "ручка: " + string.Join(", ", _knobHits.Select(UiText.Title));
        Log($"Итог: {(Result?.ToString() ?? "не найден")}; тестовых букв от клавиш — {hits}; {knob}");
        _text.Text = Result != null
            ? $"Клавиатура понимает вариант «{Variants[Array.FindIndex(Variants, v => v.Mode == Result)].Title}». " +
              "Нажмите «Готово» — программа сохранит этот способ и предложит записать ваши настройки."
            : "Ни один вариант не заставил клавиши печатать буквы. Всё, что прислала клавиатура, сохранено " +
              "в user_data\\monitor.log — этого хватит, чтобы разобраться дальше.";
        _received.Text = "";
        _primary.Text = Result != null ? "Готово" : "Закрыть";
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
            DialogResult = Result != null ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }

    void OnSkip()
    {
        if (_busy || _index < 0 || _index >= _steps.Count) return;
        if (_steps[_index] is ReplugStep) Log("Переподключение пропущено");
        if (_steps[_index] is PressStep) FinishPress();
        else Next();
    }

    /// <summary>Тестовая раскладка: каждой кнопке — своя буква этого варианта.</summary>
    static AppSettings TestSettings(Variant variant)
    {
        var settings = new AppSettings { Protocol = variant.Mode };
        settings.Normalize();
        var layer = settings.Profile.Layers[0];
        for (int i = 0; i < KeyIds.Length; i++) layer.Actions[KeyIds[i]] = PadAction.FromKeys(KeyChord.Of(Code(variant.KeyLetters[i])));
        for (int i = 0; i < KnobIds.Length; i++) layer.Actions[KnobIds[i]] = PadAction.FromKeys(KeyChord.Of(Code(variant.KnobLetters[i])));
        settings.EnsureTriggers();
        return settings;
    }

    static byte Code(char c) => c is >= '1' and <= '9' ? (byte)(0x1E + c - '1') : c == '0' ? (byte)0x27 : (byte)(0x04 + c - 'a');

    static char? Letter(byte code) =>
        code is >= 0x04 and <= 0x1D ? (char)('a' + code - 0x04) : code is >= 0x1E and <= 0x26 ? (char)('1' + code - 0x1E) : code == 0x27 ? '0' : null;

    // ───────────────────────── Ввод с клавиатуры ─────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (RawInput.Register(Handle) is string error) Log("ОШИБКА подписки на ввод: " + error);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RawInput.Unregister();
        _quiet.Dispose();
        _replugSettle.Dispose();
        Log("──── Диагностика закрыта");
        _file?.Dispose();
        base.OnFormClosed(e);
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
            RawInput.DeviceName(input.Device).Contains(DeviceTag, StringComparison.OrdinalIgnoreCase))
        {
            OnPadInput(input);
        }
        else if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            BeginInvoke(OnDeviceChange);
        }
        base.WndProc(ref m);
    }

    void OnPadInput(RawEvent input)
    {
        string? text = input switch
        {
            RawKeyboardEvent key => $"{(key.Up ? "↑" : "↓")}{key.Key}",
            RawHidEvent hid => hid.Report.Any(b => b != 0) ? $"мм[{Convert.ToHexString(hid.Report)}]" : "мм0",
            RawMouseEvent mouse when mouse.ButtonFlags != 0 => $"мышь{mouse.ButtonFlags:X4}{(mouse.WheelDelta != 0 ? $"({mouse.WheelDelta})" : "")}",
            _ => null,
        };
        if (text == null) return;

        if (_index >= 0 && _index < _steps.Count && _steps[_index] is PressStep press)
        {
            _captured.Add(text);
            _received.Text = "Получено: " + string.Join("  ", _captured);
            _quiet.Stop();
            _quiet.Start();
            if (input is RawKeyboardEvent { Up: false } down) CountLetter(press, down.Key);
        }
        else
        {
            Log("вне шага: " + text);
        }
    }

    void CountLetter(PressStep press, Keys key)
    {
        if (press.Variant < 0) return;
        char? letter = Letter(KeyCatalog.FromVirtualKey(key));
        if (letter == null) return;
        var variant = Variants[press.Variant];
        if (variant.KeyLetters.Contains(letter.Value))
        {
            _keyHits[press.Variant]++;
            Result ??= variant.Mode;
        }
        if (variant.KnobLetters.Contains(letter.Value)) _knobHits.Add(KnobIds[variant.KnobLetters.IndexOf(letter.Value)]);
    }

    void OnDeviceChange()
    {
        UpdateDeviceStatus();
        bool connected = MacroPadDevice.FindConfigInterface() != null;
        if (_index < 0 || _index >= _steps.Count || _steps[_index] is not ReplugStep) return;
        if (!connected && !_sawRemoval)
        {
            _sawRemoval = true;
            Log("Плата отключена");
            _text.Text = "Теперь подключите кабель обратно.";
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
        _device.Text = connected ? "● Клавиатура подключена" : "○ Клавиатура отключена";
        _device.ForeColor = connected ? Theme.Success : Theme.Danger;
    }

    void Log(string text)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  Мастер  {text}";
        _log.Items.Add(line);
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        try
        {
            _file?.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }
}
