using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>То, что устройство само «нажимает» при срабатывании кнопки.</summary>
public abstract record HardwareAction;

/// <summary>До 5 нажатий подряд. Modifiers — битовая маска HID (Ctrl=1, Shift=2, Alt=4, Win=8), Code — HID-код клавиши.</summary>
public sealed record KeysAction(IReadOnlyList<(byte Modifiers, byte Code)> Presses) : HardwareAction;

/// <summary>Клавиша из HID Consumer Page (громкость, медиа и т.п.).</summary>
public sealed record MediaAction(ushort Code) : HardwareAction;

/// <summary>Щелчок мыши. Buttons: левая=1, правая=2, средняя=4. Modifier: Ctrl=1, Shift=2, Alt=4.</summary>
public sealed record MouseClickAction(byte Buttons, byte Modifier) : HardwareAction;

/// <summary>Прокрутка колеса: положительное значение — вверх.</summary>
public sealed record MouseWheelAction(sbyte Delta, byte Modifier) : HardwareAction;

/// <summary>
/// Протокол программирования клавиатур CH552/CH57x с USB ID 1189:8890.
/// Пакеты по 64 байта отправляются в служебный HID-интерфейс (MI_01).
/// Формат совместим с открытым проектом kriomant/ch57x-keyboard-tool.
/// </summary>
public static class K8890Protocol
{
    public const int PacketSize = 64;
    public const int MaxPresses = 5;

    /// <summary>Пустой пакет, который отправляется сразу после открытия устройства.</summary>
    public static byte[] InitPacket() => new byte[PacketSize];

    /// <summary>
    /// Пакеты, назначающие действие кнопке прошивки <paramref name="key"/>: клавиши нумеруются с 1 построчно,
    /// ручки — после 12 клавиш: 13 + 3 × номер ручки + (влево 0, нажатие 1, вправо 2). <paramref name="layer"/> — с нуля.
    /// </summary>
    public static List<byte[]> Bind(int layer, byte key, HardwareAction action)
    {
        // Номер слоя занимает старшую половину байта: 0–14.
        if (layer is < 0 or > 14) throw new ArgumentOutOfRangeException(nameof(layer));

        byte layerBits = (byte)((layer + 1) << 4);
        var packets = new List<byte[]> { Packet(0x03, 0xFE, (byte)(layer + 1), 0x01, 0x01) };

        switch (action)
        {
            case KeysAction keys:
            {
                if (keys.Presses.Count > MaxPresses)
                    throw new ArgumentException(L.F("protocol.tooManyPresses", "Не больше {0} нажатий подряд.", MaxPresses), nameof(action));

                byte count = (byte)keys.Presses.Count;
                // Прошивка ожидает «пустое» нажатие с индексом 0 перед реальными.
                packets.Add(Packet(0x03, key, (byte)(layerBits | 0x01), count, 0, 0, 0));
                for (int i = 0; i < keys.Presses.Count; i++)
                {
                    var (modifiers, code) = keys.Presses[i];
                    packets.Add(Packet(0x03, key, (byte)(layerBits | 0x01), count, (byte)(i + 1), modifiers, code));
                }
                break;
            }
            case MediaAction media:
                packets.Add(Packet(0x03, key, (byte)(layerBits | 0x02), (byte)(media.Code & 0xFF), (byte)(media.Code >> 8)));
                break;
            case MouseClickAction click:
                packets.Add(Packet(0x03, key, (byte)(layerBits | 0x03), click.Buttons, 0, 0, 0, click.Modifier));
                break;
            case MouseWheelAction wheel:
                packets.Add(Packet(0x03, key, (byte)(layerBits | 0x03), 0, 0, 0, unchecked((byte)wheel.Delta), wheel.Modifier));
                break;
            default:
                throw new ArgumentException(L.T("protocol.unknownAction", "Неизвестный тип действия."), nameof(action));
        }

        packets.Add(Packet(0x03, 0xAA, 0xAA));
        return packets;
    }

    static byte[] Packet(params byte[] bytes)
    {
        var packet = new byte[PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }
}
