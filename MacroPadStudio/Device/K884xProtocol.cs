using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>
/// Формат k884x (по kriomant/ch57x-keyboard-tool) для плат 1189:8840, 1189:8842, 1189:8850 и 514C:8851:
/// до 15 клавиш и 3 ручек, слои. Кнопка описывается одним кадром, после каждой — команды записи.
///   клавиши      03 FE [кнопка] [слой+1] 01 00×5 [кол-во] ([модификаторы] [код])…
///   мультимедиа  03 FE [кнопка] [слой+1] 02 00×5 00 [код мл.] [код ст.]
///   мышь         03 FE [кнопка] [слой+1] 03 00×5 01 [мод] [кнопки]   или   03 [мод] 00 00 00 [колесо]
///   запись       03 AA AA,  03 FD FE FF,  03 AA AA
/// Номера кнопок: клавиши 1–15, ручки 16 + 3 × номер ручки + (влево 0, нажатие 1, вправо 2).
/// </summary>
public static class K884xProtocol
{
    public const int MaxPresses = 18;

    public static List<byte[]> Bind(int layer, byte key, HardwareAction action)
    {
        if (layer is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(layer));

        var packet = new byte[K8890Protocol.PacketSize];
        packet[0] = 0x03;
        packet[1] = 0xFE;
        packet[2] = key;
        packet[3] = (byte)(layer + 1);
        switch (action)
        {
            case KeysAction keys:
            {
                if (keys.Presses.Count > MaxPresses)
                    throw new ArgumentException(L.F("protocol.tooManyPresses", "Не больше {0} нажатий подряд.", MaxPresses), nameof(action));
                packet[4] = 0x01;
                // Одно нажатие без клавиши (только модификаторы) прошивка ждёт с количеством 0.
                packet[10] = (byte)(keys.Presses.Count == 1 && keys.Presses[0].Code == 0 ? 0 : keys.Presses.Count);
                for (int i = 0; i < keys.Presses.Count; i++)
                {
                    packet[11 + 2 * i] = keys.Presses[i].Modifiers;
                    packet[12 + 2 * i] = keys.Presses[i].Code;
                }
                break;
            }
            case MediaAction media:
                packet[4] = 0x02;
                packet[11] = (byte)(media.Code & 0xFF);
                packet[12] = (byte)(media.Code >> 8);
                break;
            case MouseClickAction click:
                packet[4] = 0x03;
                packet[10] = 0x01;
                packet[11] = click.Modifier;
                packet[12] = click.Buttons;
                break;
            case MouseWheelAction wheel:
                packet[4] = 0x03;
                packet[10] = 0x03;
                packet[11] = wheel.Modifier;
                packet[15] = unchecked((byte)wheel.Delta);
                break;
            default:
                throw new ArgumentException(L.T("protocol.unknownAction", "Неизвестный тип действия."), nameof(action));
        }

        return new List<byte[]> { packet, Packet(0x03, 0xAA, 0xAA), Packet(0x03, 0xFD, 0xFE, 0xFF), Packet(0x03, 0xAA, 0xAA) };
    }

    static byte[] Packet(params byte[] bytes)
    {
        var packet = new byte[K8890Protocol.PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }
}
