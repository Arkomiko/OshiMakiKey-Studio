namespace OshiMakiKey.Model;

/// <summary>Клавиша: HID-код, короткое имя («Ctrl + C»), имя для списка и виртуальный код Windows для записи с клавиатуры.</summary>
public sealed record KeyInfo(byte Code, string Name, string ListName, Keys VirtualKey);

public static class KeyCatalog
{
    public const byte C = 0x06;
    public const byte V = 0x19;
    public const byte Z = 0x1D;
    public const byte PrintScreen = 0x46;

    public static IReadOnlyList<KeyInfo> All { get; }

    static readonly Dictionary<byte, KeyInfo> ByCode;
    static readonly Dictionary<Keys, byte> ByVirtualKey;

    static KeyCatalog()
    {
        var list = new List<KeyInfo>();
        void Add(byte code, string name, Keys vk, string? listName = null) => list.Add(new KeyInfo(code, name, listName ?? name, vk));

        // Подсказка: какая русская буква на той же клавише (раскладка ЙЦУКЕН).
        const string russian = "ФИСВУАПРШОЛДЬТЩЗЙКЫЕГМЦЧНЯ";
        for (int i = 0; i < 26; i++)
        {
            string letter = ((char)('A' + i)).ToString();
            Add((byte)(0x04 + i), letter, Keys.A + i, $"{letter}   ({russian[i]})");
        }
        for (int d = 1; d <= 9; d++) Add((byte)(0x1E + d - 1), d.ToString(), Keys.D1 + (d - 1));
        Add(0x27, "0", Keys.D0);

        Add(0x28, "Enter", Keys.Enter);
        Add(0x29, "Esc", Keys.Escape);
        Add(0x2A, "Backspace", Keys.Back);
        Add(0x2B, "Tab", Keys.Tab);
        Add(0x2C, "Пробел", Keys.Space);
        Add(0x2D, "-", Keys.OemMinus, "-   (минус)");
        Add(0x2E, "=", Keys.Oemplus, "=   (равно)");
        Add(0x2F, "[", Keys.OemOpenBrackets, "[   (Х)");
        Add(0x30, "]", Keys.OemCloseBrackets, "]   (Ъ)");
        Add(0x31, "\\", Keys.OemPipe, "\\   (обратная косая)");
        Add(0x33, ";", Keys.OemSemicolon, ";   (Ж)");
        Add(0x34, "'", Keys.OemQuotes, "'   (Э)");
        Add(0x35, "`", Keys.Oemtilde, "`   (Ё)");
        Add(0x36, ",", Keys.Oemcomma, ",   (Б)");
        Add(0x37, ".", Keys.OemPeriod, ".   (Ю)");
        Add(0x38, "/", Keys.OemQuestion, "/   (точка в русской)");
        Add(0x39, "Caps Lock", Keys.CapsLock);

        for (int i = 0; i < 12; i++) Add((byte)(0x3A + i), $"F{i + 1}", Keys.F1 + i);

        Add(PrintScreen, "Print Screen", Keys.PrintScreen);
        Add(0x47, "Scroll Lock", Keys.Scroll);
        Add(0x48, "Pause", Keys.Pause);
        Add(0x49, "Insert", Keys.Insert);
        Add(0x4A, "Home", Keys.Home);
        Add(0x4B, "Page Up", Keys.PageUp);
        Add(0x4C, "Delete", Keys.Delete);
        Add(0x4D, "End", Keys.End);
        Add(0x4E, "Page Down", Keys.PageDown);
        Add(0x4F, "→", Keys.Right, "→   Стрелка вправо");
        Add(0x50, "←", Keys.Left, "←   Стрелка влево");
        Add(0x51, "↓", Keys.Down, "↓   Стрелка вниз");
        Add(0x52, "↑", Keys.Up, "↑   Стрелка вверх");

        Add(0x53, "Num Lock", Keys.NumLock);
        Add(0x54, "Num /", Keys.Divide);
        Add(0x55, "Num *", Keys.Multiply);
        Add(0x56, "Num -", Keys.Subtract);
        Add(0x57, "Num +", Keys.Add);
        Add(0x58, "Num Enter", Keys.None);
        for (int d = 1; d <= 9; d++) Add((byte)(0x59 + d - 1), $"Num {d}", Keys.NumPad1 + (d - 1));
        Add(0x62, "Num 0", Keys.NumPad0);
        Add(0x63, "Num .", Keys.Decimal);
        Add(0x64, "\\ (102)", Keys.OemBackslash, "\\   (клавиша 102)");
        Add(0x65, "Menu", Keys.Apps, "Menu   (контекстное меню)");

        for (int i = 0; i < 12; i++) Add((byte)(0x68 + i), $"F{i + 13}", Keys.F13 + i);

        All = list;
        ByCode = list.ToDictionary(k => k.Code);
        ByVirtualKey = new Dictionary<Keys, byte>();
        foreach (var key in list)
            if (key.VirtualKey != Keys.None) ByVirtualKey.TryAdd(key.VirtualKey, key.Code);
    }

    public static string NameOf(byte code) => ByCode.TryGetValue(code, out var key) ? key.Name : $"<{code}>";

    /// <summary>Виртуальный код Windows для HID-кода (для эмуляции нажатий), Keys.None — если его нет.</summary>
    public static Keys VirtualKeyOf(byte code) =>
        code == 0x58 ? Keys.Enter : ByCode.TryGetValue(code, out var key) ? key.VirtualKey : Keys.None;

    /// <summary>HID-код по клавише Windows, 0 — если клавиша не поддерживается.</summary>
    public static byte FromVirtualKey(Keys vk) => ByVirtualKey.TryGetValue(vk, out var code) ? code : (byte)0;
}

/// <summary>Мультимедийная клавиша (HID Consumer Page), которую поддерживает Windows.</summary>
public sealed record MediaInfo(ushort Code, string Name, string ShortName)
{
    public override string ToString() => Name;
}

public static class MediaCatalog
{
    public const ushort PlayPause = 0xCD;
    public const ushort NextTrack = 0xB5;
    public const ushort PreviousTrack = 0xB6;
    public const ushort Mute = 0xE2;
    public const ushort VolumeUp = 0xE9;
    public const ushort VolumeDown = 0xEA;

    public static IReadOnlyList<MediaInfo> All { get; } = new MediaInfo[]
    {
        new(PlayPause, "Воспроизведение / пауза", "Пуск/пауза"),
        new(NextTrack, "Следующий трек", "След. трек"),
        new(PreviousTrack, "Предыдущий трек", "Пред. трек"),
        new(0xB7, "Стоп", "Стоп"),
        new(VolumeUp, "Громкость +", "Громк. +"),
        new(VolumeDown, "Громкость −", "Громк. −"),
        new(Mute, "Выключить / включить звук", "Без звука"),
        new(0x6F, "Яркость экрана +", "Яркость +"),
        new(0x70, "Яркость экрана −", "Яркость −"),
        new(0x192, "Калькулятор", "Калькулятор"),
        new(0x194, "Проводник («Этот компьютер»)", "Проводник"),
        new(0x18A, "Почта", "Почта"),
        new(0x183, "Медиаплеер", "Плеер"),
        new(0x221, "Поиск", "Поиск"),
        new(0x223, "Браузер: домашняя страница", "Домой"),
        new(0x224, "Браузер: назад", "Назад"),
        new(0x225, "Браузер: вперёд", "Вперёд"),
        new(0x227, "Браузер: обновить страницу", "Обновить"),
        new(0x22A, "Браузер: избранное", "Избранное"),
    };

    public static MediaInfo? Find(ushort code) => All.FirstOrDefault(m => m.Code == code);

    public static string NameOf(ushort code) => Find(code)?.Name ?? $"Код 0x{code:X3}";

    // Раскладка битового мультимедиа-интерфейса платы (снята через HidP_SetUsages с реального устройства).
    static readonly ushort[] ConsumerBits = { 0xEA, 0xE9, 0xE2, 0xB5, 0xB6, 0xCD, 0x18A, 0x183, 0x223, 0xB7, 0x70, 0x6F };

    /// <summary>Номер бита в отчёте мультимедиа платы, null — у платы нет такой клавиши.</summary>
    public static int? ConsumerBit(ushort code)
    {
        int index = Array.IndexOf(ConsumerBits, code);
        return index >= 0 ? index : null;
    }
}
