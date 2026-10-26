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
        ActionKind.Keys => Chords(action, " + ", ", ") ?? NotAssigned,
        ActionKind.Media => MediaCatalog.NameOf(action.MediaCode),
        ActionKind.Mouse => MouseName(action.Mouse) + WheelSuffix(action) + ModifierSuffix(action.MouseModifier, " + "),
        ActionKind.System => SystemSummary(action),
        _ => NotAssigned,
    };

    static string NotAssigned => L.T("action.none", "Не назначено");

    /// <summary>Очень короткая подпись для изображения клавиши.</summary>
    public static string Compact(PadAction action) => action.Kind switch
    {
        ActionKind.Keys => Chords(action, "+", ", ") ?? "—",
        ActionKind.Media => MediaCatalog.Find(action.MediaCode)?.ShortName ?? L.T("action.media.short", "Медиа"),
        ActionKind.Mouse => action.Mouse switch
        {
            MouseActionKind.LeftClick => L.T("mouse.left.short", "ЛКМ"),
            MouseActionKind.RightClick => L.T("mouse.right.short", "ПКМ"),
            MouseActionKind.MiddleClick => L.T("mouse.middle.short", "СКМ"),
            MouseActionKind.WheelUp => L.T("mouse.wheelUp.short", "Колесо ↑"),
            _ => L.T("mouse.wheelDown.short", "Колесо ↓"),
        } + ModifierSuffix(action.MouseModifier, "+"),
        ActionKind.System => action.SystemAction switch
        {
            SystemActionKind.RunProgram => L.T("system.RunProgram.short", "Запуск"),
            SystemActionKind.OpenUrl => L.T("system.OpenUrl.short", "Сайт"),
            SystemActionKind.OpenPath => L.T("system.OpenPath.short", "Открыть"),
            SystemActionKind.TypeText => L.T("system.TypeText.short", "Текст"),
            SystemActionKind.RunCommand => L.T("system.RunCommand.short", "Команда"),
            SystemActionKind.LockPc => L.T("system.LockPc.short", "Блокировка"),
            SystemActionKind.MonitorOff => L.T("system.MonitorOff.short", "Экран"),
            SystemActionKind.Sleep => L.T("system.Sleep.short", "Сон"),
            SystemActionKind.Shutdown => L.T("system.Shutdown.short", "Выкл. ПК"),
            _ => L.T("system.Restart.short", "Перезагр."),
        },
        _ => "—",
    };

    public static string MouseName(MouseActionKind kind) => kind switch
    {
        MouseActionKind.LeftClick => L.T("mouse.left", "Щелчок левой кнопкой"),
        MouseActionKind.RightClick => L.T("mouse.right", "Щелчок правой кнопкой"),
        MouseActionKind.MiddleClick => L.T("mouse.middle", "Щелчок средней кнопкой"),
        MouseActionKind.WheelUp => L.T("mouse.wheelUp", "Колесо вверх"),
        _ => L.T("mouse.wheelDown", "Колесо вниз"),
    };

    public static string SystemName(SystemActionKind kind) => kind switch
    {
        SystemActionKind.RunProgram => L.T("system.RunProgram", "Запустить программу"),
        SystemActionKind.OpenUrl => L.T("system.OpenUrl", "Открыть сайт"),
        SystemActionKind.OpenPath => L.T("system.OpenPath", "Открыть файл или папку"),
        SystemActionKind.TypeText => L.T("system.TypeText", "Напечатать текст"),
        SystemActionKind.RunCommand => L.T("system.RunCommand", "Выполнить команду"),
        SystemActionKind.LockPc => L.T("system.LockPc", "Заблокировать компьютер"),
        SystemActionKind.MonitorOff => L.T("system.MonitorOff", "Выключить экран"),
        SystemActionKind.Sleep => L.T("system.Sleep", "Спящий режим"),
        SystemActionKind.Shutdown => L.T("system.Shutdown", "Выключить компьютер"),
        _ => L.T("system.Restart", "Перезагрузить компьютер"),
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
        SystemActionKind.RunProgram => L.F("summary.run", "Запуск: {0}", FileTitle(action.Target)),
        SystemActionKind.OpenUrl => L.F("summary.url", "Сайт: {0}", OrNotSet(action.Target)),
        SystemActionKind.OpenPath => L.F("summary.open", "Открыть: {0}", FileTitle(action.Target)),
        SystemActionKind.TypeText => L.F("summary.text", "Текст: «{0}»", OrNotSet(action.Text)),
        SystemActionKind.RunCommand => L.F("summary.command", "Команда: {0}", OrNotSet(action.Target)),
        _ => SystemName(action.SystemAction),
    };

    static string OrNotSet(string value)
    {
        string line = value.ReplaceLineEndings(" ").Trim();
        return line.Length == 0 ? L.T("summary.notSet", "не указано") : line;
    }

    static string FileTitle(string path)
    {
        string trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        if (trimmed.Length == 0) return L.T("summary.notChosen", "не выбрано");
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }
}
