using OshiMakiKey.Model;

namespace OshiMakiKey.Agent;

/// <summary>
/// Отличает удержание клавиши от новых нажатий. Прошивка платы клавишу не «держит»: на нажатие она сразу шлёт
/// короткий тап (нажата → через ~50 мс отпущена), а пока клавиша зажата, повторяет такие тапы — первый через
/// ~300 мс, дальше каждые ~110 мс. Для Windows это отдельные нажатия, поэтому, например, «звук вкл/выкл»
/// переключался бы по кругу. Тапы одной служебной клавиши, идущие чаще <see cref="HoldGap"/>, считаются
/// удержанием: переключатели срабатывают один раз, а громкость, яркость и колесо мыши повторяются, как на
/// обычной клавиатуре.
/// </summary>
internal sealed class HoldFilter
{
    /// <summary>Пауза между тапами, после которой нажатие считается новым.</summary>
    public const int HoldGap = 400;

    readonly Dictionary<int, long> _lastTap = new();
    readonly Func<long> _clock;

    public HoldFilter(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    /// <summary>Выполнять ли действие на этот тап служебной клавиши <paramref name="slot"/>.</summary>
    public bool ShouldRun(int slot, ControlId control, PadAction action)
    {
        long now = _clock();
        bool held = _lastTap.TryGetValue(slot, out long last) && now - last < HoldGap;
        _lastTap[slot] = now;
        return !held || Execution.RepeatsWhileHeld(action, control);
    }
}
