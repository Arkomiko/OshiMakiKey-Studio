using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>
/// Формат k8850 (по kriomant/ch57x-keyboard-tool) для платы 514C:8850 с сеткой 4×4. Модификаторы передаются
/// отдельными «клавишами» F1–F4 (Ctrl, Shift, Alt, Win), после каждой кнопки — команда записи.
///   клавиши      03 FD [кнопка] [слой+1] 01 00 [кол-во] (00 00 [значение])…
///   мультимедиа  03 FD [кнопка] [слой+1] 02 00 02 00 00 [код мл.] 00 00 [код ст.]
///   мышь         03 FD [кнопка] [слой+1] 03 01 04 00 00 [мод] 00 00 [кнопки] 00 00 [dx] 00 00 [dy] 00 00 [колесо]
///   запись       03 FD FE FF
/// Номера кнопок: клавиши 1–16, ручки 17 + 3 × номер ручки + (влево 0, нажатие 1, вправо 2).
/// Тестовых кадров в исходниках нет — формат помечен как непроверенный.
/// </summary>
public static class K8850Protocol
{
    public const int MaxEntries = 18;

    static readonly (byte Mask, byte Code)[] ModifierCodes = { (0x01, 0xF1), (0x02, 0xF2), (0x04, 0xF3), (0x08, 0xF4) };

    public static List<byte[]> Bind(int layer, byte key, HardwareAction action)
    {
        if (layer is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(layer));

        var packet = new byte[K8890Protocol.PacketSize];
        packet[0] = 0x03;
        packet[1] = 0xFD;
        packet[2] = key;
        packet[3] = (byte)(layer + 1);
        switch (action)
        {
            case KeysAction keys:
            {
                var entries = new List<byte>();
                foreach (var (modifiers, code) in keys.Presses)
                {
                    entries.AddRange(ModifierCodes.Where(m => (modifiers & m.Mask) != 0).Select(m => m.Code));
                    entries.Add(code);
                }
                if (entries.Count > MaxEntries)
                    throw new ArgumentException(L.F("protocol.tooManyPresses", "Не больше {0} нажатий подряд.", MaxEntries), nameof(action));
                packet[4] = 0x01;
                packet[6] = (byte)entries.Count;
                for (int i = 0; i < entries.Count; i++) packet[9 + 3 * i] = entries[i];
                break;
            }
            case MediaAction media:
                packet[4] = 0x02;
                packet[6] = 0x02;
                packet[9] = (byte)(media.Code & 0xFF);
                packet[12] = (byte)(media.Code >> 8);
                break;
            case MouseClickAction click:
                MouseHeader(packet, click.Modifier);
                packet[12] = click.Buttons;
                break;
            case MouseWheelAction wheel:
                MouseHeader(packet, wheel.Modifier);
                packet[21] = unchecked((byte)wheel.Delta);
                break;
            default:
                throw new ArgumentException(L.T("protocol.unknownAction", "Неизвестный тип действия."), nameof(action));
        }

        var finish = new byte[K8890Protocol.PacketSize];
        new byte[] { 0x03, 0xFD, 0xFE, 0xFF }.CopyTo(finish, 0);
        return new List<byte[]> { packet, finish };
    }

    static void MouseHeader(byte[] packet, byte modifier)
    {
        packet[4] = 0x03;
        packet[5] = 0x01;
        packet[6] = 0x04;
        // Модификатор мыши: Ctrl F1, Shift F2, Alt F3.
        packet[9] = modifier switch { 0x01 => 0xF1, 0x02 => 0xF2, 0x04 => 0xF3, _ => 0x00 };
    }
}
