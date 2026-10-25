using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>Элементы управления устройства: 6 клавиш (сетка 3×2) и три действия ручки.</summary>
public enum ControlId { Key1, Key2, Key3, Key4, Key5, Key6, KnobLeft, KnobPress, KnobRight }

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

    public string Name { get; set; } = "Мой профиль";
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
        Name ??= "Мой профиль";
        Layers ??= new();
        Layers.RemoveAll(l => l == null);
        while (Layers.Count < MaxLayers) Layers.Add(new Layer());
        if (Layers.Count > MaxLayers) Layers.RemoveRange(MaxLayers, Layers.Count - MaxLayers);

        foreach (var layer in Layers)
        {
            layer.Actions ??= new();
            foreach (var id in Enum.GetValues<ControlId>())
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

    public IEnumerable<(int Layer, ControlId Control, PadAction Action)> All(int layerCount)
    {
        for (int layer = 0; layer < Math.Min(layerCount, Layers.Count); layer++)
            foreach (var id in Enum.GetValues<ControlId>())
                yield return (layer, id, Layers[layer].Get(id));
    }

    /// <summary>
    /// Выдаёт уникальную служебную клавишу каждому действию, которое выполняет фоновый агент
    /// (<paramref name="needsAgent"/>). Уже назначенные номера сохраняются, чтобы правка параметров
    /// действия не требовала перезаписи устройства.
    /// </summary>
    public void EnsureTriggers(Func<PadAction, bool> needsAgent)
    {
        // Сочетания, которые пользователь сам назначил кнопкам, служебными делать нельзя.
        var reserved = new HashSet<(byte, byte)>();
        foreach (var (_, _, action) in All(MaxLayers))
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
        foreach (var (_, _, action) in All(MaxLayers))
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
    public Profile Profile { get; set; } = Profile.CreateDefault();

    /// <summary>Сколько слоёв записывать в устройство (у большинства небольших моделей — один).</summary>
    public int LayerCount { get; set; } = 1;

    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Хэш последней успешно записанной в устройство конфигурации.</summary>
    public string? LastWrittenSignature { get; set; }

    /// <summary>Способ записи, выбранный пользователем (Auto — определить по устройству).</summary>
    public ProtocolMode Protocol { get; set; } = ProtocolMode.Auto;

    /// <summary>Что определилось по подключённому устройству в последний раз.</summary>
    public ProtocolMode? DetectedProtocol { get; set; }

    public ProtocolMode EffectiveProtocol =>
        Protocol != ProtocolMode.Auto ? Protocol : DetectedProtocol ?? ProtocolMode.Vendor;

    /// <summary>Слои есть только в формате ch57x — для остальных записывается только первый.</summary>
    public int EffectiveLayerCount => EffectiveProtocol == ProtocolMode.Legacy ? LayerCount : 1;

    public bool NeedsAgent(PadAction action) => Execution.NeedsAgent(action, EffectiveProtocol);

    public void EnsureTriggers() => Profile.EnsureTriggers(NeedsAgent);

    public void Normalize()
    {
        Profile ??= Profile.CreateDefault();
        Profile.Normalize();
        LayerCount = Math.Clamp(LayerCount, 1, Profile.MaxLayers);
        if (DetectedProtocol == ProtocolMode.Auto) DetectedProtocol = null;
        EnsureTriggers();
    }
}

/// <summary>Как записывать конфигурацию в устройство.</summary>
public enum ProtocolMode
{
    Auto,

    /// <summary>Формат padclaude: прошивка хранит готовый HID-отчёт клавиатуры для каждой кнопки.</summary>
    Direct,

    /// <summary>Формат ch57x-keyboard-tool (k8890): клавиши, мультимедиа и мышь разбирает сама прошивка.</summary>
    Legacy,

    /// <summary>
    /// Формат официальной программы для плат без Report ID («версия 0»): без префикса 0x03 и без слоёв.
    /// Так официальная программа пишет платы, у которых, как у CH552 6 клавиш + ручка, нет Report ID.
    /// </summary>
    Vendor,
}

/// <summary>Что устройство умеет само, а что выполняет фоновый агент программы.</summary>
public static class Execution
{
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
