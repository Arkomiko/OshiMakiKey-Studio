namespace OshiMakiKey.Model;

/// <summary>Человекочитаемые описания действий.</summary>
public static class BindingText
{
    public static string Chord(KeyChord chord, string separator = " + ")
    {
        var parts = new List<string>();
        if (chord.Ctrl) parts.Add("Ctrl");
        if (chord.Shift) parts.Add("Shift");
        if (chord.Alt) parts.Add("Alt");
        if (chord.Win) parts.Add("Win");
        if (chord.Key != 0) parts.Add(KeyCatalog.NameOf(chord.Key));
        return parts.Count == 0 ? "—" : string.Join(separator, parts);
    }

    public static string Summary(PadAction action) => action.Kind switch
    {
        ActionKind.Keys => Chords(action, " + ", ", ") ?? "Не назначено",
        ActionKind.Media => MediaCatalog.NameOf(action.MediaCode),
        ActionKind.Mouse => MouseName(action.Mouse) + WheelSuffix(action) + ModifierSuffix(action.MouseModifier, " + "),
        ActionKind.System => SystemSummary(action),
        _ => "Не назначено",
    };

    /// <summary>Очень короткая подпись для изображения клавиши.</summary>
    public static string Compact(PadAction action) => action.Kind switch
    {
        ActionKind.Keys => Chords(action, "+", ", ") ?? "—",
        ActionKind.Media => MediaCatalog.Find(action.MediaCode)?.ShortName ?? "Медиа",
        ActionKind.Mouse => action.Mouse switch
        {
            MouseActionKind.LeftClick => "ЛКМ",
            MouseActionKind.RightClick => "ПКМ",
            MouseActionKind.MiddleClick => "СКМ",
            MouseActionKind.WheelUp => "Колесо ↑",
            _ => "Колесо ↓",
        } + ModifierSuffix(action.MouseModifier, "+"),
        ActionKind.System => action.SystemAction switch
        {
            SystemActionKind.RunProgram => "Запуск",
            SystemActionKind.OpenUrl => "Сайт",
            SystemActionKind.OpenPath => "Открыть",
            SystemActionKind.TypeText => "Текст",
            SystemActionKind.RunCommand => "Команда",
            SystemActionKind.LockPc => "Блокировка",
            SystemActionKind.MonitorOff => "Экран",
            SystemActionKind.Sleep => "Сон",
            SystemActionKind.Shutdown => "Выкл. ПК",
            _ => "Перезагр.",
        },
        _ => "—",
    };

    public static string MouseName(MouseActionKind kind) => kind switch
    {
        MouseActionKind.LeftClick => "Щелчок левой кнопкой",
        MouseActionKind.RightClick => "Щелчок правой кнопкой",
        MouseActionKind.MiddleClick => "Щелчок средней кнопкой",
        MouseActionKind.WheelUp => "Колесо вверх",
        _ => "Колесо вниз",
    };

    public static string SystemName(SystemActionKind kind) => kind switch
    {
        SystemActionKind.RunProgram => "Запустить программу",
        SystemActionKind.OpenUrl => "Открыть сайт",
        SystemActionKind.OpenPath => "Открыть файл или папку",
        SystemActionKind.TypeText => "Напечатать текст",
        SystemActionKind.RunCommand => "Выполнить команду",
        SystemActionKind.LockPc => "Заблокировать компьютер",
        SystemActionKind.MonitorOff => "Выключить экран",
        SystemActionKind.Sleep => "Спящий режим",
        SystemActionKind.Shutdown => "Выключить компьютер",
        _ => "Перезагрузить компьютер",
    };

    static string? Chords(PadAction action, string keySeparator, string chordSeparator)
    {
        var chords = action.Chords.Where(c => !c.IsEmpty).Select(c => Chord(c, keySeparator)).ToList();
        return chords.Count == 0 ? null : string.Join(chordSeparator, chords);
    }

    static string WheelSuffix(PadAction action) =>
        (action.Mouse is MouseActionKind.WheelUp or MouseActionKind.WheelDown) && action.WheelStep > 1 ? $" ×{action.WheelStep}" : "";

    static string ModifierSuffix(MouseModifier modifier, string separator) =>
        modifier == MouseModifier.None ? "" : $"{separator}{modifier}";

    static string SystemSummary(PadAction action) => action.SystemAction switch
    {
        SystemActionKind.RunProgram => "Запуск: " + FileTitle(action.Target),
        SystemActionKind.OpenUrl => "Сайт: " + OrNotSet(action.Target),
        SystemActionKind.OpenPath => "Открыть: " + FileTitle(action.Target),
        SystemActionKind.TypeText => "Текст: «" + OrNotSet(action.Text) + "»",
        SystemActionKind.RunCommand => "Команда: " + OrNotSet(action.Target),
        _ => SystemName(action.SystemAction),
    };

    static string OrNotSet(string value)
    {
        string line = value.ReplaceLineEndings(" ").Trim();
        return line.Length == 0 ? "не указано" : line;
    }

    static string FileTitle(string path)
    {
        string trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        if (trimmed.Length == 0) return "не выбрано";
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }
}
