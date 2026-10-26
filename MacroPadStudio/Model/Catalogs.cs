namespace OshiMakiKey.Model;

/// <summary>Клавиша: HID-код, короткое имя («Ctrl + C»), имя для списка и виртуальный код Windows для записи с клавиатуры.</summary>
public sealed record KeyInfo(byte Code, string Name, string ListName, Keys VirtualKey);

public static class KeyCatalog
{
    public const byte C = 0x06;
    public const byte V = 0x19;
    public const byte Z = 0x1D;
    public const byte PrintScreen = 0x46;

    static List<KeyInfo> _all = new();
    static Dictionary<byte, KeyInfo> _byCode = new();
    static readonly Dictionary<Keys, byte> ByVirtualKey = new();

    static KeyCatalog() => Rebuild();

    public static IReadOnlyList<KeyInfo> All => _all;

    /// <summary>Пересобирает названия под текущий язык.</summary>
    public static void Rebuild()
    {
        var list = new List<KeyInfo>();
        void Add(byte code, string name, Keys vk, string? listName = null) => list.Add(new KeyInfo(code, name, listName ?? name, vk));

        // Подсказка: какая русская буква на той же клавише (раскладка ЙЦУКЕН). В английском переводе её нет.
        const string russian = "ФИСВУАПРШОЛДЬТЩЗЙКЫЕГМЦЧНЯ";
        string letterList = L.T("key.letterList", "{0}   ({1})");
        for (int i = 0; i < 26; i++)
        {
            string letter = ((char)('A' + i)).ToString();
            Add((byte)(0x04 + i), letter, Keys.A + i, string.Format(letterList, letter, russian[i]));
        }
        for (int d = 1; d <= 9; d++) Add((byte)(0x1E + d - 1), d.ToString(), Keys.D1 + (d - 1));
        Add(0x27, "0", Keys.D0);

        Add(0x28, "Enter", Keys.Enter);
        Add(0x29, "Esc", Keys.Escape);
        Add(0x2A, "Backspace", Keys.Back);
        Add(0x2B, "Tab", Keys.Tab);
        Add(0x2C, L.T("key.space", "Пробел"), Keys.Space);
        Add(0x2D, "-", Keys.OemMinus, L.T("key.list.minus", "-   (минус)"));
        Add(0x2E, "=", Keys.Oemplus, L.T("key.list.equal", "=   (равно)"));
        Add(0x2F, "[", Keys.OemOpenBrackets, L.T("key.list.lbracket", "[   (Х)"));
        Add(0x30, "]", Keys.OemCloseBrackets, L.T("key.list.rbracket", "]   (Ъ)"));
        Add(0x31, "\\", Keys.OemPipe, L.T("key.list.backslash", "\\   (обратная косая)"));
        Add(0x33, ";", Keys.OemSemicolon, L.T("key.list.semicolon", ";   (Ж)"));
        Add(0x34, "'", Keys.OemQuotes, L.T("key.list.quote", "'   (Э)"));
        Add(0x35, "`", Keys.Oemtilde, L.T("key.list.grave", "`   (Ё)"));
        Add(0x36, ",", Keys.Oemcomma, L.T("key.list.comma", ",   (Б)"));
        Add(0x37, ".", Keys.OemPeriod, L.T("key.list.period", ".   (Ю)"));
        Add(0x38, "/", Keys.OemQuestion, L.T("key.list.slash", "/   (точка в русской)"));
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
        Add(0x4F, "→", Keys.Right, L.T("key.list.right", "→   Стрелка вправо"));
        Add(0x50, "←", Keys.Left, L.T("key.list.left", "←   Стрелка влево"));
        Add(0x51, "↓", Keys.Down, L.T("key.list.down", "↓   Стрелка вниз"));
        Add(0x52, "↑", Keys.Up, L.T("key.list.up", "↑   Стрелка вверх"));

        Add(0x53, "Num Lock", Keys.NumLock);
        Add(0x54, "Num /", Keys.Divide);
        Add(0x55, "Num *", Keys.Multiply);
        Add(0x56, "Num -", Keys.Subtract);
        Add(0x57, "Num +", Keys.Add);
        Add(0x58, "Num Enter", Keys.None);
        for (int d = 1; d <= 9; d++) Add((byte)(0x59 + d - 1), $"Num {d}", Keys.NumPad1 + (d - 1));
        Add(0x62, "Num 0", Keys.NumPad0);
        Add(0x63, "Num .", Keys.Decimal);
        Add(0x64, "\\ (102)", Keys.OemBackslash, L.T("key.list.oem102", "\\   (клавиша 102)"));
        Add(0x65, "Menu", Keys.Apps, L.T("key.list.menu", "Menu   (контекстное меню)"));

        for (int i = 0; i < 12; i++) Add((byte)(0x68 + i), $"F{i + 13}", Keys.F13 + i);

        _all = list;
        _byCode = list.ToDictionary(k => k.Code);
        ByVirtualKey.Clear();
        foreach (var key in list)
            if (key.VirtualKey != Keys.None) ByVirtualKey.TryAdd(key.VirtualKey, key.Code);
    }

    public static string NameOf(byte code) => _byCode.TryGetValue(code, out var key) ? key.Name : $"<{code}>";

    /// <summary>Виртуальный код Windows для HID-кода (для эмуляции нажатий), Keys.None — если его нет.</summary>
    public static Keys VirtualKeyOf(byte code) =>
        code == 0x58 ? Keys.Enter : _byCode.TryGetValue(code, out var key) ? key.VirtualKey : Keys.None;

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

    static List<MediaInfo> _all = new();

    static MediaCatalog() => Rebuild();

    public static IReadOnlyList<MediaInfo> All => _all;

    /// <summary>Пересобирает названия под текущий язык.</summary>
    public static void Rebuild() => _all = new List<MediaInfo>
    {
        new(PlayPause, L.T("media.playpause", "Воспроизведение / пауза"), L.T("media.playpause.short", "Пуск/пауза")),
        new(NextTrack, L.T("media.next", "Следующий трек"), L.T("media.next.short", "След. трек")),
        new(PreviousTrack, L.T("media.prev", "Предыдущий трек"), L.T("media.prev.short", "Пред. трек")),
        new(0xB7, L.T("media.stop", "Стоп"), L.T("media.stop.short", "Стоп")),
        new(VolumeUp, L.T("media.volup", "Громкость +"), L.T("media.volup.short", "Громк. +")),
        new(VolumeDown, L.T("media.voldown", "Громкость −"), L.T("media.voldown.short", "Громк. −")),
        new(Mute, L.T("media.mute", "Выключить / включить звук"), L.T("media.mute.short", "Без звука")),
        new(0x6F, L.T("media.brightup", "Яркость экрана +"), L.T("media.brightup.short", "Яркость +")),
        new(0x70, L.T("media.brightdown", "Яркость экрана −"), L.T("media.brightdown.short", "Яркость −")),
        new(0x192, L.T("media.calc", "Калькулятор"), L.T("media.calc.short", "Калькулятор")),
        new(0x194, L.T("media.explorer", "Проводник («Этот компьютер»)"), L.T("media.explorer.short", "Проводник")),
        new(0x18A, L.T("media.mail", "Почта"), L.T("media.mail.short", "Почта")),
        new(0x183, L.T("media.player", "Медиаплеер"), L.T("media.player.short", "Плеер")),
        new(0x221, L.T("media.search", "Поиск"), L.T("media.search.short", "Поиск")),
        new(0x223, L.T("media.home", "Браузер: домашняя страница"), L.T("media.home.short", "Домой")),
        new(0x224, L.T("media.back", "Браузер: назад"), L.T("media.back.short", "Назад")),
        new(0x225, L.T("media.forward", "Браузер: вперёд"), L.T("media.forward.short", "Вперёд")),
        new(0x227, L.T("media.refresh", "Браузер: обновить страницу"), L.T("media.refresh.short", "Обновить")),
        new(0x22A, L.T("media.favorites", "Браузер: избранное"), L.T("media.favorites.short", "Избранное")),
    };

    public static MediaInfo? Find(ushort code) => _all.FirstOrDefault(m => m.Code == code);

    public static string NameOf(ushort code) => Find(code)?.Name ?? L.F("media.unknown", "Код 0x{0:X3}", code);

    // Раскладка битового мультимедиа-интерфейса платы (снята через HidP_SetUsages с реального устройства).
    static readonly ushort[] ConsumerBits = { 0xEA, 0xE9, 0xE2, 0xB5, 0xB6, 0xCD, 0x18A, 0x183, 0x223, 0xB7, 0x70, 0x6F };

    /// <summary>Номер бита в отчёте мультимедиа платы, null — у платы нет такой клавиши.</summary>
    public static int? ConsumerBit(ushort code)
    {
        int index = Array.IndexOf(ConsumerBits, code);
        return index >= 0 ? index : null;
    }
}
