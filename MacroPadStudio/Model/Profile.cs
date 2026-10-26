using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>
/// Элементы управления: до 16 клавиш и до 3 ручек по три действия (поворот влево, нажатие, поворот вправо).
/// Какие из них есть на плате, говорит её драйвер (<see cref="DriverDef.Controls"/>). В файлах хранятся имена,
/// поэтому порядок можно менять; имена первой ручки (KnobLeft…) остались от версий с одной ручкой.
/// </summary>
public enum ControlId
{
    Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Key10, Key11, Key12, Key13, Key14, Key15, Key16,
    KnobLeft, KnobPress, KnobRight,
    Knob2Left, Knob2Press, Knob2Right,
    Knob3Left, Knob3Press, Knob3Right,
}

public static class Controls
{
    public const int MaxKeys = 16;
    public const int MaxKnobs = 3;

    public static bool IsKey(ControlId id) => id <= ControlId.Key16;

    /// <summary>Номер клавиши с нуля.</summary>
    public static int KeyIndex(ControlId id) => id - ControlId.Key1;

    public static ControlId Key(int index) => ControlId.Key1 + index;

    /// <summary>Номер ручки с нуля.</summary>
    public static int KnobIndex(ControlId id) => (id - ControlId.KnobLeft) / 3;

    /// <summary>0 — поворот влево, 1 — нажатие, 2 — поворот вправо.</summary>
    public static int KnobAction(ControlId id) => (id - ControlId.KnobLeft) % 3;

    public static ControlId Knob(int knob, int action) => ControlId.KnobLeft + knob * 3 + action;

    /// <summary>Клавиши и ручки платы с такой раскладкой, по порядку: клавиши построчно, затем ручки.</summary>
    public static List<ControlId> Of(int keys, int knobs) =>
        Enumerable.Range(0, Math.Clamp(keys, 0, MaxKeys)).Select(Key)
            .Concat(Enumerable.Range(0, Math.Clamp(knobs, 0, MaxKnobs) * 3).Select(i => Knob(i / 3, i % 3)))
            .ToList();
}

public enum ActionKind { None, Keys, Media, Mouse, System }

public enum MouseActionKind { LeftClick, RightClick, MiddleClick, WheelUp, WheelDown }

public enum MouseModifier { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public enum SystemActionKind { RunProgram, OpenUrl, OpenPath, TypeText, RunCommand, LockPc, MonitorOff, Sleep, Shutdown, Restart }

/// <summary>Одно нажатие: модификаторы + клавиша (HID-код).</summary>
public sealed class KeyChord
{
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    /// <summary>HID-код клавиши (Keyboard page). 0 — только модификаторы.</summary>
    public byte Key { get; set; }

    [JsonIgnore]
    public byte ModifierMask => (byte)((Ctrl ? 0x01 : 0) | (Shift ? 0x02 : 0) | (Alt ? 0x04 : 0) | (Win ? 0x08 : 0));

    [JsonIgnore]
    public bool IsEmpty => Key == 0 && ModifierMask == 0;

    public static KeyChord Of(byte key, bool ctrl = false, bool shift = false, bool alt = false, bool win = false) =>
        new() { Key = key, Ctrl = ctrl, Shift = shift, Alt = alt, Win = win };
}

/// <summary>Действие, назначенное кнопке или ручке.</summary>
public sealed class PadAction
{
    public const int MaxChords = 5;

    public ActionKind Kind { get; set; }

    // Kind == Keys
    public List<KeyChord> Chords { get; set; } = new();

    // Kind == Media
    public ushort MediaCode { get; set; }

    // Kind == Mouse
    public MouseActionKind Mouse { get; set; }
    public int WheelStep { get; set; } = 1;
    public MouseModifier MouseModifier { get; set; }

    // Kind == System
    public SystemActionKind SystemAction { get; set; }
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Номер служебной клавиши (F13–F24 с модификаторами), которую устройство отправляет для системного действия.</summary>
    public int? TriggerSlot { get; set; }

    public static PadAction FromKeys(params KeyChord[] chords) => new() { Kind = ActionKind.Keys, Chords = chords.ToList() };

    public static PadAction FromMedia(ushort code) => new() { Kind = ActionKind.Media, MediaCode = code };
}

public sealed class Layer
{
    public Dictionary<ControlId, PadAction> Actions { get; set; } = new();

    public PadAction Get(ControlId id)
    {
        if (!Actions.TryGetValue(id, out var action) || action == null)
            Actions[id] = action = new PadAction();
        return action;
    }
}

public sealed class Profile
{
    public const int MaxLayers = 3;

    public string Name { get; set; } = "";
    public List<Layer> Layers { get; set; } = new();

    public static Profile CreateDefault()
    {
        var profile = new Profile();
        profile.Normalize();
        var layer = profile.Layers[0];
        layer.Actions[ControlId.Key1] = PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true));
        layer.Actions[ControlId.Key2] = PadAction.FromKeys(KeyChord.Of(KeyCatalog.V, ctrl: true));
        layer.Actions[ControlId.Key3] = PadAction.FromKeys(KeyChord.Of(KeyCatalog.Z, ctrl: true));
        layer.Actions[ControlId.Key4] = PadAction.FromMedia(MediaCatalog.PreviousTrack);
        layer.Actions[ControlId.Key5] = PadAction.FromMedia(MediaCatalog.PlayPause);
        layer.Actions[ControlId.Key6] = PadAction.FromMedia(MediaCatalog.NextTrack);
        layer.Actions[ControlId.KnobLeft] = PadAction.FromMedia(MediaCatalog.VolumeDown);
        layer.Actions[ControlId.KnobPress] = PadAction.FromMedia(MediaCatalog.Mute);
        layer.Actions[ControlId.KnobRight] = PadAction.FromMedia(MediaCatalog.VolumeUp);
        return profile;
    }

    /// <summary>Чинит данные после загрузки из файла: все слои и кнопки на месте, значения в допустимых пределах.</summary>
    public void Normalize()
    {
        Name ??= "";
        Layers ??= new();
        Layers.RemoveAll(l => l == null);
        while (Layers.Count < MaxLayers) Layers.Add(new Layer());
        if (Layers.Count > MaxLayers) Layers.RemoveRange(MaxLayers, Layers.Count - MaxLayers);

        foreach (var layer in Layers)
        {
            layer.Actions ??= new();
            // Действия создаются по требованию (Layer.Get), поэтому чиним только те, что есть в файле.
            foreach (var id in layer.Actions.Keys.ToList())
            {
                var action = layer.Get(id);
                action.Chords ??= new();
                action.Chords.RemoveAll(c => c == null);
                if (action.Chords.Count > PadAction.MaxChords)
                    action.Chords.RemoveRange(PadAction.MaxChords, action.Chords.Count - PadAction.MaxChords);
                action.Target ??= "";
                action.Arguments ??= "";
                action.Text ??= "";
                action.WheelStep = Math.Clamp(action.WheelStep, 1, 20);
            }
        }
    }

    /// <summary>Действия перечисленных кнопок платы в первых <paramref name="layerCount"/> слоях.</summary>
    public IEnumerable<(int Layer, ControlId Control, PadAction Action)> All(int layerCount, IReadOnlyList<ControlId> controls)
    {
        for (int layer = 0; layer < Math.Min(layerCount, Layers.Count); layer++)
            foreach (var id in controls)
                yield return (layer, id, Layers[layer].Get(id));
    }

    /// <summary>
    /// Выдаёт уникальную служебную клавишу каждому действию, которое выполняет фоновый агент
    /// (<paramref name="needsAgent"/>). Уже назначенные номера сохраняются, чтобы правка параметров
    /// действия не требовала перезаписи устройства.
    /// </summary>
    public void EnsureTriggers(Func<PadAction, bool> needsAgent, IReadOnlyList<ControlId> controls)
    {
        // Кнопки, которых нет на плате, служебных клавиш не получают.
        foreach (var layer in Layers)
            foreach (var (id, action) in layer.Actions)
                if (!controls.Contains(id)) action.TriggerSlot = null;

        // Сочетания, которые пользователь сам назначил кнопкам, служебными делать нельзя.
        var reserved = new HashSet<(byte, byte)>();
        foreach (var (_, _, action) in All(MaxLayers, controls))
            if (action.Kind == ActionKind.Keys)
                foreach (var chord in action.Chords)
                    reserved.Add((chord.ModifierMask, chord.Key));

        var used = new HashSet<int>();
        bool IsFree(int slot)
        {
            var chord = TriggerKeys.Chord(slot);
            return !used.Contains(slot) && !reserved.Contains((chord.ModifierMask, chord.Key));
        }

        var pending = new List<PadAction>();
        foreach (var (_, _, action) in All(MaxLayers, controls))
        {
            if (!needsAgent(action))
            {
                action.TriggerSlot = null;
                continue;
            }
            if (action.TriggerSlot is int slot && slot >= 0 && slot < TriggerKeys.SlotCount && IsFree(slot))
                used.Add(slot);
            else
                pending.Add(action);
        }

        foreach (var action in pending)
        {
            int slot = Enumerable.Range(0, TriggerKeys.SlotCount).FirstOrDefault(IsFree, -1);
            action.TriggerSlot = slot >= 0 ? slot : null;
            if (slot >= 0) used.Add(slot);
        }
    }
}

public sealed class AppSettings
{
    /// <summary>Раскладка активного пресета (хранится в user_data/presets, а не в settings.json).</summary>
    [JsonIgnore]
    public Profile Profile { get; set; } = Profile.CreateDefault();

    /// <summary>Раскладка из settings.json старой версии — переносится в пресет при первом запуске.</summary>
    [JsonPropertyName("Profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Profile? LegacyProfile { get; set; }

    /// <summary>Название активного пресета (имя файла в user_data/presets).</summary>
    public string? ActivePreset { get; set; }

    /// <summary>Идентификатор темы из папки theme.</summary>
    public string? Theme { get; set; }

    /// <summary>Код языка из папки language.</summary>
    public string? Language { get; set; }

    /// <summary>Сколько слоёв записывать в устройство (у большинства небольших моделей — один).</summary>
    public int LayerCount { get; set; } = 1;

    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Хэш последней успешно записанной в устройство конфигурации.</summary>
    public string? LastWrittenSignature { get; set; }

    /// <summary>Мастер первого запуска пройден.</summary>
    public bool SetupDone { get; set; }

    /// <summary>Тур по окну показан.</summary>
    public bool TourDone { get; set; }

    /// <summary>Спрашивать, прежде чем перетаскивание перезапишет назначенную кнопку.</summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>
    /// Не повторять сочетание, пока клавиша зажата: сочетания тоже выполняет программа (служебная клавиша
    /// регистрируется без автоповтора), а не прошивка, которую Windows повторяет как обычную клавиатуру.
    /// </summary>
    public bool NoKeyRepeat { get; set; }

    /// <summary>Пресет, с которым работает фоновая служба OMK_Studio-Service; null — активный.</summary>
    public string? ServicePreset { get; set; }

    /// <summary>Пользователь попросил больше не предлагать установить службу.</summary>
    public bool ServicePromptDismissed { get; set; }

    /// <summary>Драйвер, выбранный вручную; null — определять по подключённой плате.</summary>
    public string? DriverId { get; set; }

    /// <summary>Драйвер, подобранный по плате при последнем подключении.</summary>
    public string? DetectedDriverId { get; set; }

    /// <summary>Номера кнопок ручки, найденные мастером проверки записи, — отдельно для каждого драйвера.</summary>
    public Dictionary<string, List<int>>? LearnedKnobSlots { get; set; }

    // Поля прежних версий: читаются для переноса настроек, обратно не записываются.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProtocolMode Protocol { get; set; } = ProtocolMode.Auto;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProtocolMode? DetectedProtocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? KnobSlots { get; set; }

    [JsonIgnore]
    public DriverDef EffectiveDriver => DriverStore.Find(DriverId) ?? DriverStore.Find(DetectedDriverId) ?? DriverStore.Default;

    [JsonIgnore]
    public ProtocolMode EffectiveProtocol => EffectiveDriver.Protocol;

    /// <summary>Клавиши и ручки подключённой (или выбранной) платы.</summary>
    [JsonIgnore]
    public IReadOnlyList<ControlId> Controls => EffectiveDriver.Controls;

    /// <summary>Номер кнопки в прошивке: из драйвера, для ручек — с учётом найденного мастером.</summary>
    public byte SlotOf(ControlId control)
    {
        var driver = EffectiveDriver;
        if (!Model.Controls.IsKey(control) && LearnedKnobSlots != null && LearnedKnobSlots.TryGetValue(driver.Id, out var learned))
        {
            int index = Model.Controls.KnobIndex(control) * 3 + Model.Controls.KnobAction(control);
            if (learned.Count == driver.Layout.Knobs * 3 && learned[index] is > 0 and < 0xA0) return (byte)learned[index];
        }
        return driver.SlotOf(control);
    }

    /// <summary>Слои есть не во всех форматах — для остальных записывается только первый.</summary>
    [JsonIgnore]
    public int EffectiveLayerCount => Execution.SupportsLayers(EffectiveProtocol) ? LayerCount : 1;

    public bool NeedsAgent(PadAction action) =>
        Execution.NeedsAgent(action, EffectiveProtocol) ||
        (NoKeyRepeat && action.Kind == ActionKind.Keys && action.Chords.Any(c => !c.IsEmpty));

    public void EnsureTriggers() => Profile.EnsureTriggers(NeedsAgent, Controls);

    public void Normalize()
    {
        Profile ??= Profile.CreateDefault();
        Profile.Normalize();
        LayerCount = Math.Clamp(LayerCount, 1, Profile.MaxLayers);

        // Перенос из версий, где вместо драйверов был выбор способа записи.
        if (DriverId == null && Protocol != ProtocolMode.Auto) DriverId = DriverStore.FirstWith(Protocol)?.Id;
        if (DetectedDriverId == null && DetectedProtocol is ProtocolMode detected and not ProtocolMode.Auto)
            DetectedDriverId = DriverStore.FirstWith(detected)?.Id;
        if (KnobSlots is { Count: 3 } && DriverStore.Find("ch552-6keys-knob") is { } direct)
        {
            LearnedKnobSlots ??= new();
            LearnedKnobSlots.TryAdd(direct.Id, KnobSlots);
        }
        Protocol = ProtocolMode.Auto;
        DetectedProtocol = null;
        KnobSlots = null;

        EnsureTriggers();
    }
}

/// <summary>Как записывать конфигурацию в устройство.</summary>
public enum ProtocolMode
{
    Auto,

    /// <summary>
    /// Прямой: кадр [кнопка][готовый HID-отчёт клавиатуры]. Так работает CH552 6 клавиш + ручка с битовым
    /// мультимедиа-интерфейсом — сама плата хранит одно сочетание клавиш на кнопку.
    /// </summary>
    Direct,

    /// <summary>Формат ch57x-keyboard-tool (k8890): клавиши, мультимедиа и мышь разбирает сама прошивка.</summary>
    Legacy,

    /// <summary>Формат официальной программы для плат без Report ID («версия 0»): без префикса 0x03 и без слоёв.</summary>
    Vendor,

    /// <summary>Формат k884x (ch57x-keyboard-tool): платы 1189:8840/8842/8850 и 514C:8851, до 15 клавиш и 3 ручек.</summary>
    K884x,

    /// <summary>Формат k8850 (ch57x-keyboard-tool): плата 514C:8850 с сеткой 4×4.</summary>
    K8850,
}

/// <summary>Что устройство умеет само, а что выполняет фоновый агент программы.</summary>
public static class Execution
{
    /// <summary>Прошивка хранит несколько слоёв (переключаются на самой плате).</summary>
    public static bool SupportsLayers(ProtocolMode protocol) =>
        protocol is ProtocolMode.Legacy or ProtocolMode.K884x or ProtocolMode.K8850;

    /// <summary>
    /// Повторять ли действие, пока кнопка зажата, — как на обычной клавиатуре: громкость, яркость и колесо мыши
    /// повторяются, переключатели (звук вкл/выкл, пауза, треки, запуск, сочетания) — один раз. Повороты ручки —
    /// это отдельные щелчки, их не фильтруем никогда.
    /// </summary>
    public static bool RepeatsWhileHeld(PadAction action, ControlId control)
    {
        if (!Controls.IsKey(control) && Controls.KnobAction(control) != 1) return true;
        return action.Kind switch
        {
            ActionKind.Media => action.MediaCode is MediaCatalog.VolumeUp or MediaCatalog.VolumeDown or 0x6F or 0x70,
            ActionKind.Mouse => action.Mouse is MouseActionKind.WheelUp or MouseActionKind.WheelDown,
            _ => false,
        };
    }
    public static bool NeedsAgent(PadAction action, ProtocolMode protocol) => action.Kind switch
    {
        ActionKind.None => false,
        ActionKind.System => true,
        // Прямая прошивка хранит один отчёт клавиатуры: одно сочетание — да, последовательность — нет.
        ActionKind.Keys => protocol == ProtocolMode.Direct && action.Chords.Count(c => !c.IsEmpty) > 1,
        // Официальный формат умеет только те мультимедиа-клавиши, что есть в отчёте платы.
        ActionKind.Media => protocol == ProtocolMode.Direct ||
                            (protocol == ProtocolMode.Vendor && MediaCatalog.ConsumerBit(action.MediaCode) == null),
        _ => protocol == ProtocolMode.Direct,
    };
}

/// <summary>
/// Служебные клавиши для системных действий: F13–F24, затем Shift+F13…, затем Ctrl+F13….
/// На обычных клавиатурах этих клавиш нет, поэтому они ни с чем не конфликтуют.
/// </summary>
public static class TriggerKeys
{
    public const int SlotCount = 36;
    const byte FirstHidCode = 0x68; // F13
    const int FirstVirtualKey = 0x7C; // VK_F13

    public static bool HasShift(int slot) => slot / 12 == 1;
    public static bool HasCtrl(int slot) => slot / 12 == 2;
    public static int VirtualKey(int slot) => FirstVirtualKey + slot % 12;

    public static KeyChord Chord(int slot) =>
        KeyChord.Of((byte)(FirstHidCode + slot % 12), ctrl: HasCtrl(slot), shift: HasShift(slot));

    public static string Describe(int slot) => BindingText.Chord(Chord(slot));
}
