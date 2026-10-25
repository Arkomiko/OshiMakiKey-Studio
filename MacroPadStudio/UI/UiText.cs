using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

internal static class UiText
{
    public static bool IsKey(ControlId id) => id <= ControlId.Key6;

    public static int KeyNumber(ControlId id) => id - ControlId.Key1 + 1;

    public static string Title(ControlId id) => id switch
    {
        _ when IsKey(id) => $"Клавиша {KeyNumber(id)}",
        ControlId.KnobLeft => "Ручка: поворот влево",
        ControlId.KnobPress => "Ручка: нажатие",
        _ => "Ручка: поворот вправо",
    };

    public static string Subtitle(ControlId id) => id switch
    {
        ControlId.KnobLeft => "Срабатывает на каждый щелчок ручки против часовой стрелки.",
        ControlId.KnobPress => "Срабатывает при нажатии на ручку.",
        ControlId.KnobRight => "Срабатывает на каждый щелчок ручки по часовой стрелке.",
        _ => "Что произойдёт при нажатии этой клавиши.",
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
        SystemActionKind.RunProgram => "Запускает выбранную программу (.exe, .bat, ярлык .lnk). Аргументы указывать не обязательно.",
        SystemActionKind.OpenUrl => "Открывает адрес в браузере по умолчанию.",
        SystemActionKind.OpenPath => "Открывает файл в связанной с ним программе или папку в Проводнике.",
        SystemActionKind.TypeText => "Печатает текст в активном окне: подписи, шаблоны ответов, e-mail. Подходит любой язык и эмодзи.",
        SystemActionKind.RunCommand => "Выполняет команду командной строки (cmd) в скрытом окне.",
        SystemActionKind.LockPc => "Блокирует компьютер, как сочетание Win + L.",
        SystemActionKind.MonitorOff => "Выключает экран. Он включится от движения мыши или нажатия клавиши.",
        SystemActionKind.Sleep => "Переводит компьютер в спящий режим.",
        SystemActionKind.Shutdown => "Выключает компьютер. Перед выключением спросит подтверждение.",
        _ => "Перезагружает компьютер. Перед перезагрузкой спросит подтверждение.",
    };
}

/// <summary>Элемент выпадающего списка: значение + подпись.</summary>
internal sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}
