using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

internal static class UiText
{
    public static bool IsKey(ControlId id) => id <= ControlId.Key6;

    public static int KeyNumber(ControlId id) => id - ControlId.Key1 + 1;

    public static string Title(ControlId id) => id switch
    {
        _ when IsKey(id) => L.F("control.key", "Клавиша {0}", KeyNumber(id)),
        ControlId.KnobLeft => L.T("control.knobLeft", "Ручка: поворот влево"),
        ControlId.KnobPress => L.T("control.knobPress", "Ручка: нажатие"),
        _ => L.T("control.knobRight", "Ручка: поворот вправо"),
    };

    public static string Subtitle(ControlId id) => id switch
    {
        ControlId.KnobLeft => L.T("control.knobLeft.hint", "Срабатывает на каждый щелчок ручки против часовой стрелки."),
        ControlId.KnobPress => L.T("control.knobPress.hint", "Срабатывает при нажатии на ручку."),
        ControlId.KnobRight => L.T("control.knobRight.hint", "Срабатывает на каждый щелчок ручки по часовой стрелке."),
        _ => L.T("control.key.hint", "Что произойдёт при нажатии этой клавиши."),
    };

    public static string Glyph(ControlId id) => id switch
    {
        _ when IsKey(id) => KeyNumber(id).ToString(),
        ControlId.KnobLeft => "↺",
        ControlId.KnobPress => "●",
        _ => "↻",
    };

    public static string SystemDescription(SystemActionKind kind) => kind switch
    {
        SystemActionKind.RunProgram => L.T("system.RunProgram.hint", "Запускает выбранную программу (.exe, .bat, ярлык .lnk). Аргументы указывать не обязательно."),
        SystemActionKind.OpenUrl => L.T("system.OpenUrl.hint", "Открывает адрес в браузере по умолчанию."),
        SystemActionKind.OpenPath => L.T("system.OpenPath.hint", "Открывает файл в связанной с ним программе или папку в Проводнике."),
        SystemActionKind.TypeText => L.T("system.TypeText.hint", "Печатает текст в активном окне: подписи, шаблоны ответов, e-mail. Подходит любой язык и эмодзи."),
        SystemActionKind.RunCommand => L.T("system.RunCommand.hint", "Выполняет команду командной строки (cmd) в скрытом окне."),
        SystemActionKind.LockPc => L.T("system.LockPc.hint", "Блокирует компьютер, как сочетание Win + L."),
        SystemActionKind.MonitorOff => L.T("system.MonitorOff.hint", "Выключает экран. Он включится от движения мыши или нажатия клавиши."),
        SystemActionKind.Sleep => L.T("system.Sleep.hint", "Переводит компьютер в спящий режим."),
        SystemActionKind.Shutdown => L.T("system.Shutdown.hint", "Выключает компьютер. Перед выключением спросит подтверждение."),
        _ => L.T("system.Restart.hint", "Перезагружает компьютер. Перед перезагрузкой спросит подтверждение."),
    };
}

/// <summary>Элемент выпадающего списка: значение + подпись.</summary>
internal sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}
