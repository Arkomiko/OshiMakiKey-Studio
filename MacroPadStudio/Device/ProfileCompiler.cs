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

    public static List<byte[]> Compile(AppSettings settings) => settings.EffectiveProtocol switch
    {
        ProtocolMode.Direct => CompileDirect(settings),
        ProtocolMode.Vendor => CompileVendor(settings),
        _ => CompileLegacy(settings),
    };

    static List<byte[]> CompileVendor(AppSettings settings) =>
        Enum.GetValues<ControlId>()
            .SelectMany(id => VendorProtocol.Bind(id, ToHardware(settings.Profile.Layers[0].Get(id))))
            .ToList();

    public static string Signature(IEnumerable<byte[]> packets) =>
        Convert.ToHexString(SHA256.HashData(packets.SelectMany(p => p).ToArray()));

    static List<byte[]> CompileDirect(AppSettings settings)
    {
        var packets = new List<byte[]> { DirectProtocol.Open() };
        foreach (var id in Enum.GetValues<ControlId>())
        {
            var action = settings.Profile.Layers[0].Get(id);
            var chord = action.TriggerSlot is int slot
                ? TriggerKeys.Chord(slot)
                : action.Kind == ActionKind.Keys ? action.Chords.FirstOrDefault(c => !c.IsEmpty) : null;

            packets.Add(chord == null
                ? DirectProtocol.Bind(id, 0, Array.Empty<byte>())
                : DirectProtocol.Bind(id, chord.ModifierMask, chord.Key == 0 ? Array.Empty<byte>() : new[] { chord.Key }));
        }
        packets.Add(DirectProtocol.Close());
        return packets;
    }

    static List<byte[]> CompileLegacy(AppSettings settings)
    {
        var packets = new List<byte[]>();
        for (int layer = 0; layer < settings.EffectiveLayerCount; layer++)
            foreach (var id in Enum.GetValues<ControlId>())
                packets.AddRange(K8890Protocol.Bind(layer, id, ToHardware(settings.Profile.Layers[layer].Get(id))));
        return packets;
    }

    public static HardwareAction ToHardware(PadAction action)
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
                    .Take(K8890Protocol.MaxPresses)
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
