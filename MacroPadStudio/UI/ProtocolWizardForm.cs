using System.Text;
using OshiMakiKey.Agent;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// «Мастер подбора способа записи». Платы с одним и тем же USB ID 1189:8890 прошиты по-разному, и узнать
/// формат можно только опытом: мастер по очереди записывает каждый вариант со своим набором тестовых букв,
/// просит переподключить кабель и нажать клавишу 1, а по пришедшей букве понимает, какой вариант сработал.
/// </summary>
internal sealed class ProtocolWizardForm : Form
{
    sealed record Variant(ProtocolMode Mode, string Title, string KeyLetters, string KnobLetters);

    // Буквы у вариантов не пересекаются: по одной нажатой клавише видно, чья запись сработала.
    static readonly Variant[] Variants =
    {
        new(ProtocolMode.Direct, "прямой (новая ревизия CH552)", "abcdef", "ghi"),
        new(ProtocolMode.Vendor, "как у официальной программы", "jklmno", "pqr"),
        new(ProtocolMode.Legacy, "формат ch57x-keyboard-tool", "stuvwx", "yz1"),
    };

    static readonly ControlId[] KeyIds = { ControlId.Key1, ControlId.Key2, ControlId.Key3, ControlId.Key4, ControlId.Key5, ControlId.Key6 };
    static readonly ControlId[] KnobIds = { ControlId.KnobLeft, ControlId.KnobPress, ControlId.KnobRight };

    enum Stage { Intro, Writing, WaitKey, Knob, Done, Failed }

    const string DeviceTag = "VID_1189&PID_8890";

    readonly Label _title = new();
    readonly Label _text = new();
    readonly Label _device = new();
    readonly Label _received = new();
    readonly ListBox _log = new();
    readonly Button _primary = new();
    readonly Button _skip = new();
    readonly StreamWriter? _file;
    readonly HashSet<ControlId> _knobSeen = new();
    Stage _stage = Stage.Intro;
    int _variant;

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
        ClientSize = new Size(Theme.S(640), Theme.S(560));
        KeyPreview = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(16)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _title.AutoSize = true;
        _title.Font = Theme.Title;
        _title.Margin = new Padding(0, 0, 0, Theme.S(10));
        foreach (var label in new[] { _text, _device, _received })
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(Theme.S(590), 0);
            label.Margin = new Padding(0, 0, 0, Theme.S(10));
        }
        _device.Font = Theme.Strong;
        _received.Font = Theme.Strong;
        _received.ForeColor = Theme.AccentDark;

        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 9f);
        _log.IntegralHeight = false;

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(10), 0, 0) };
        StyleButton(_primary, primary: true);
        StyleButton(_skip, primary: false);
        _primary.Click += (_, _) => OnPrimary();
        _skip.Click += (_, _) => OnSkip();
        buttons.Controls.Add(_primary);
        buttons.Controls.Add(_skip);

        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_text, 0, 1);
        root.Controls.Add(_device, 0, 2);
        root.Controls.Add(_received, 0, 3);
        root.Controls.Add(_log, 0, 4);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);

        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            _file = new StreamWriter(Path.Combine(SettingsStore.Folder, "monitor.log"), append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (IOException)
        {
            _file = null;
        }

        Log($"──── Мастер подбора открыт {DateTime.Now:dd.MM.yyyy HH:mm:ss}, {Branding.AppName} {Branding.Version}");
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

    // ───────────────────────── Шаги ─────────────────────────

    void ShowIntro()
    {
        _stage = Stage.Intro;
        _title.Text = "Подбор способа записи";
        _text.Text = "Платы с этим USB ID прошиты по-разному. Мастер по очереди запишет до трёх вариантов " +
                     "с тестовыми буквами и после каждого попросит:\n\n" +
                     "  1. отключить USB-кабель клавиатуры и подключить снова;\n" +
                     "  2. нажать клавишу 1.\n\n" +
                     "Пока идёт подбор, клавиши будут печатать буквы — это нормально. В конце мастер запишет ваши настройки.";
        _received.Text = "";
        _primary.Text = "Начать";
        _primary.Visible = true;
        _skip.Visible = false;
    }

    async void StartVariant(int index)
    {
        _variant = index;
        var variant = Variants[index];
        _stage = Stage.Writing;
        _title.Text = $"Вариант {index + 1} из {Variants.Length}: {variant.Title}";
        _text.Text = "Записываю тестовые буквы в клавиатуру…";
        _received.Text = "";
        _primary.Visible = false;
        _skip.Visible = false;

        var device = MacroPadDevice.FindConfigInterface();
        if (device == null)
        {
            _text.Text = "Клавиатура не найдена. Подключите её USB-кабелем — запись начнётся сама.";
            _stage = Stage.Intro;
            _primary.Text = "Повторить";
            _primary.Visible = true;
            return;
        }

        var packets = ProfileCompiler.Compile(TestSettings(variant));
        bool sendInit = ProfileCompiler.SendsInitPacket(variant.Mode);
        try
        {
            await Task.Run(() =>
            {
                using var pad = MacroPadDevice.Open(device);
                pad.Write(packets, sendInit);
            });
            Log($"Записан вариант {index + 1} ({variant.Mode}): клавиши 1–6 → {variant.KeyLetters}, ручка ↺●↻ → {variant.KnobLetters}, кадров {packets.Count}");
        }
        catch (Exception ex)
        {
            Log($"Ошибка записи варианта {index + 1}: {ex.Message}");
            _text.Text = "Не удалось записать: " + ex.Message;
            _skip.Text = "Следующий вариант";
            _skip.Visible = true;
            return;
        }

        _stage = Stage.WaitKey;
        _text.Text = "Записано.\n\n  1. Отключите USB-кабель клавиатуры и подключите снова.\n  2. Нажмите клавишу 1.\n\n" +
                     "Если клавиша 1 ничего не печатает (и после переподключения тоже) — нажмите «Ничего не происходит».";
        _skip.Text = "Ничего не происходит";
        _skip.Visible = true;
    }

    void StartKnobTest(int index)
    {
        _variant = index;
        _stage = Stage.Knob;
        _knobSeen.Clear();
        Result = Variants[index].Mode;
        _title.Text = "Нашёлся рабочий способ!";
        _text.Text = $"Клавиатура понимает вариант «{Variants[index].Title}».\n\n" +
                     "Теперь проверим ручку: поверните её влево, затем вправо и нажмите на неё. " +
                     "Потом нажмите «Готово» — даже если ручка ничего не делает: это тоже важный результат, он сохранится в журнале.";
        _received.Text = "";
        _primary.Text = "Готово";
        _primary.Visible = true;
        _skip.Visible = false;
        Log($"Рабочий вариант: {Variants[index].Mode}");
    }

    void Finish()
    {
        string knob = _knobSeen.Count == 0 ? "ручка не ответила" : "ручка: " + string.Join(", ", _knobSeen.Select(UiText.Title));
        Log($"Итог: вариант {Result}, {knob}");
        _stage = Stage.Done;
        DialogResult = DialogResult.OK;
        Close();
    }

    void ShowFailed()
    {
        _stage = Stage.Failed;
        Result = null;
        _title.Text = "Подходящий вариант не найден";
        _text.Text = "Ни один из вариантов не заставил клавиатуру печатать. Пришлите файл user_data\\monitor.log — " +
                     "в нём всё, что удалось увидеть, этого хватит, чтобы разобраться дальше.";
        _primary.Text = "Закрыть";
        _primary.Visible = true;
        _skip.Visible = false;
        Log("Итог: ни один вариант не сработал");
    }

    void OnPrimary()
    {
        switch (_stage)
        {
            case Stage.Intro: StartVariant(0); break;
            case Stage.Knob: Finish(); break;
            default: Close(); break;
        }
    }

    void OnSkip()
    {
        Log($"Вариант {_variant + 1}: клавиша 1 не ответила");
        if (_variant + 1 < Variants.Length) StartVariant(_variant + 1);
        else ShowFailed();
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
        Log("──── Мастер закрыт");
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
            BeginInvoke(UpdateDeviceStatus);
        }
        base.WndProc(ref m);
    }

    void OnPadInput(RawEvent input)
    {
        switch (input)
        {
            case RawKeyboardEvent key:
                Log($"★ клавиатура {(key.Up ? "↑" : "↓")} {key.Key} (скан-код {key.MakeCode:X2})");
                if (!key.Up) OnPadKey(key.Key);
                break;
            case RawHidEvent hid:
                Log($"★ мультимедиа [{Convert.ToHexString(hid.Report)}]");
                _received.Text = "Клавиатура прислала мультимедиа-событие (не тестовую букву)";
                break;
            case RawMouseEvent mouse when mouse.ButtonFlags != 0:
                Log($"★ мышь, флаги кнопок 0x{mouse.ButtonFlags:X4}");
                break;
        }
    }

    void OnPadKey(Keys key)
    {
        byte code = KeyCatalog.FromVirtualKey(key);
        char? letter = Letter(code);
        int variant = letter == null ? -1 : Array.FindIndex(Variants, v => v.KeyLetters.Contains(letter.Value) || v.KnobLetters.Contains(letter.Value));

        if (variant < 0)
        {
            _received.Text = $"Клавиатура прислала: {key} — это не тестовая буква. Нажмите клавишу 1 ещё раз.";
            return;
        }

        var v = Variants[variant];
        int keyIndex = v.KeyLetters.IndexOf(letter!.Value);
        int knobIndex = v.KnobLetters.IndexOf(letter.Value);
        string where = keyIndex >= 0 ? $"клавиша {keyIndex + 1}" : UiText.Title(KnobIds[knobIndex]);
        _received.Text = $"Получено: «{letter}» — {where}, вариант {variant + 1}";
        Log($"Буква «{letter}» = {where}, вариант {variant + 1} ({v.Mode})");

        if (_stage == Stage.WaitKey) StartKnobTest(variant);
        else if (_stage == Stage.Knob && knobIndex >= 0) _knobSeen.Add(KnobIds[knobIndex]);
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
