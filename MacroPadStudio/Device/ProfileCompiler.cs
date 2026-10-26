using System.Security.Cryptography;
using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>Превращает профиль в последовательность пакетов для устройства.</summary>
public static class ProfileCompiler
{
    /// <summary>
    /// Пустой кадр перед записью шлют ch57x-keyboard-tool и официальная программа (проверка «версии»);
    /// для прямой прошивки он не нужен.
    /// </summary>
    public static bool SendsInitPacket(ProtocolMode protocol) => protocol != ProtocolMode.Direct;

    /// <summary>Сколько нажатий подряд умеет хранить прошивка (больше — выполняет фоновый агент).</summary>
    public static int MaxPresses(ProtocolMode protocol) => protocol switch
    {
        ProtocolMode.K884x => K884xProtocol.MaxPresses,
        ProtocolMode.Direct => 1,
        _ => K8890Protocol.MaxPresses,
    };

    public static List<byte[]> Compile(AppSettings settings) => settings.EffectiveProtocol switch
    {
        ProtocolMode.Direct => CompileDirect(settings),
        ProtocolMode.Vendor => CompileVendor(settings),
        ProtocolMode.K884x => CompileLayered(settings, K884xProtocol.Bind),
        ProtocolMode.K8850 => CompileLayered(settings, K8850Protocol.Bind),
        _ => CompileLayered(settings, K8890Protocol.Bind),
    };

    public static string Signature(IEnumerable<byte[]> packets) =>
        Convert.ToHexString(SHA256.HashData(packets.SelectMany(p => p).ToArray()));

    static List<byte[]> CompileVendor(AppSettings settings) =>
        settings.Controls
            .SelectMany(id => VendorProtocol.Bind(settings.SlotOf(id), ToHardware(settings.Profile.Layers[0].Get(id), settings.EffectiveProtocol)))
            .ToList();

    static List<byte[]> CompileDirect(AppSettings settings)
    {
        var packets = new List<byte[]>();
        foreach (var id in settings.Controls)
        {
            var action = settings.Profile.Layers[0].Get(id);
            var chord = action.TriggerSlot is int slot
                ? TriggerKeys.Chord(slot)
                : action.Kind == ActionKind.Keys ? action.Chords.FirstOrDefault(c => !c.IsEmpty) : null;

            byte button = settings.SlotOf(id);
            packets.Add(chord == null
                ? DirectProtocol.Bind(button, 0, Array.Empty<byte>())
                : DirectProtocol.Bind(button, chord.ModifierMask, chord.Key == 0 ? Array.Empty<byte>() : new[] { chord.Key }));
        }
        packets.Add(DirectProtocol.Close());
        return packets;
    }

    /// <summary>Форматы со слоями: k8890, k884x, k8850 — все слои, каждая кнопка платы.</summary>
    static List<byte[]> CompileLayered(AppSettings settings, Func<int, byte, HardwareAction, List<byte[]>> bind)
    {
        var packets = new List<byte[]>();
        for (int layer = 0; layer < settings.EffectiveLayerCount; layer++)
            foreach (var id in settings.Controls)
                packets.AddRange(bind(layer, settings.SlotOf(id), ToHardware(settings.Profile.Layers[layer].Get(id), settings.EffectiveProtocol)));
        return packets;
    }

    public static HardwareAction ToHardware(PadAction action, ProtocolMode protocol = ProtocolMode.Legacy)
    {
        // Служебная клавиша выдаётся только действиям, которые выполняет фоновый агент.
        if (action.TriggerSlot is int slot)
        {
            var chord = TriggerKeys.Chord(slot);
            return new KeysAction(new[] { (chord.ModifierMask, chord.Key) });
        }

        switch (action.Kind)
        {
            case ActionKind.Keys:
                return new KeysAction(action.Chords
                    .Where(c => !c.IsEmpty)
                    .Take(MaxPresses(protocol))
                    .Select(c => (c.ModifierMask, c.Key))
                    .ToList());

            case ActionKind.Media:
                return new MediaAction(action.MediaCode);

            case ActionKind.Mouse:
            {
                byte modifier = (byte)action.MouseModifier;
                sbyte step = (sbyte)Math.Clamp(action.WheelStep, 1, 127);
                return action.Mouse switch
                {
                    MouseActionKind.LeftClick => new MouseClickAction(0x01, modifier),
                    MouseActionKind.RightClick => new MouseClickAction(0x02, modifier),
                    MouseActionKind.MiddleClick => new MouseClickAction(0x04, modifier),
                    MouseActionKind.WheelUp => new MouseWheelAction(step, modifier),
                    _ => new MouseWheelAction((sbyte)-step, modifier),
                };
            }

            default:
                return new KeysAction(Array.Empty<(byte, byte)>());
        }
    }
}
