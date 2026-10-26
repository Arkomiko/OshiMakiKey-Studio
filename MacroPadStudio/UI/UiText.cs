using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

internal static class UiText
{
    /// <summary>Плата, которую сейчас настраивают: от её раскладки зависит, нумеровать ли ручки.</summary>
    public static DriverDef Driver { get; set; } = DriverStore.Default;

    public static bool IsKey(ControlId id) => Controls.IsKey(id);

    public static int KeyNumber(ControlId id) => Controls.KeyIndex(id) + 1;

    static string KnobActionText(ControlId id) => Controls.KnobAction(id) switch
    {
        0 => L.T("control.turnLeft", "поворот влево"),
        1 => L.T("control.press", "нажатие"),
        _ => L.T("control.turnRight", "поворот вправо"),
    };

    public static string Title(ControlId id) =>
        IsKey(id) ? L.F("control.key", "Клавиша {0}", KeyNumber(id))
        : Driver.Layout.Knobs > 1 ? L.F("control.knobN", "Ручка {0}: {1}", Controls.KnobIndex(id) + 1, KnobActionText(id))
        : L.F("control.knob", "Ручка: {0}", KnobActionText(id));

    public static string Subtitle(ControlId id) =>
        IsKey(id) ? L.T("control.key.hint", "Что произойдёт при нажатии этой клавиши.")
        : Controls.KnobAction(id) switch
        {
            0 => L.T("control.turnLeft.hint", "Срабатывает на каждый щелчок ручки против часовой стрелки."),
            1 => L.T("control.press.hint", "Срабатывает при нажатии на ручку."),
            _ => L.T("control.turnRight.hint", "Срабатывает на каждый щелчок ручки по часовой стрелке."),
        };

    public static string Glyph(ControlId id) =>
        IsKey(id) ? KeyNumber(id).ToString() : Controls.KnobAction(id) switch { 0 => "↺", 1 => "●", _ => "↻" };

    /// <summary>«12 клавиш · 3 ручки» — без склонений, чтобы одинаково работало на всех языках.</summary>
    public static string LayoutText(DriverDef driver) =>
        L.F("layout.text", "Клавиш: {0} · ручек: {1}", driver.Layout.Keys, driver.Layout.Knobs);

    public static string ProtocolName(ProtocolMode protocol) => protocol switch
    {
        ProtocolMode.Direct => L.T("protocol.name.Direct", "прямой формат"),
        ProtocolMode.Legacy => L.T("protocol.name.Legacy", "формат k8890"),
        ProtocolMode.Vendor => L.T("protocol.name.Vendor", "формат официальной программы (v0)"),
        ProtocolMode.K884x => L.T("protocol.name.K884x", "формат k884x"),
        ProtocolMode.K8850 => L.T("protocol.name.K8850", "формат k8850 (не проверен)"),
        _ => L.T("protocol.name.Auto", "автоматически"),
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
