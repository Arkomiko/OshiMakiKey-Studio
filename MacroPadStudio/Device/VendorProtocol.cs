using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>
/// Формат официальной программы «MINI KEYBOARD» для плат без Report ID («версия 0»): кадр начинается
/// прямо с номера кнопки, без префикса 0x03 и без слоёв; после каждой кнопки — команда записи во флеш.
///   клавиши      [кнопка][01][кол-во][номер][модификаторы][код]   (номер 0 — только модификаторы)
///   мультимедиа  [кнопка][02][биты отчёта мультимедиа, 2 байта]
///   мышь         [кнопка][03][кнопки][0][0][колесо][модификаторы]
///   запись       [AA][AA]
/// Восстановлено по коду rOzzy1987/MacroPad (декомпилированная официальная программа).
/// </summary>
public static class VendorProtocol
{
    public static byte[] WriteFlash() => Packet(0xAA, 0xAA);

    public static List<byte[]> Bind(ControlId control, HardwareAction action)
    {
        byte id = K8890Protocol.KeyId(control);
        var frames = new List<byte[]>();
        switch (action)
        {
            case KeysAction keys:
            {
                // Пустая кнопка записывается как одно «пустое» нажатие — так делает официальная программа.
                var presses = keys.Presses.Count == 0
                    ? new List<(byte Modifiers, byte Code)> { (0, 0) }
                    : keys.Presses.Take(K8890Protocol.MaxPresses).ToList();
                byte count = (byte)presses.Count;
                for (int i = 0; i <= presses.Count; i++)
                {
                    var (modifiers, code) = i == 0 ? (presses[0].Modifiers, (byte)0) : presses[i - 1];
                    frames.Add(Packet(id, 0x01, count, (byte)i, modifiers, code));
                }
                break;
            }
            case MediaAction media:
            {
                ushort mask = MediaCatalog.ConsumerBit(media.Code) is int bit ? (ushort)(1 << bit) : (ushort)0;
                frames.Add(Packet(id, 0x02, (byte)(mask & 0xFF), (byte)(mask >> 8)));
                break;
            }
            case MouseClickAction click:
                frames.Add(Packet(id, 0x03, click.Buttons, 0, 0, 0, click.Modifier));
                break;
            case MouseWheelAction wheel:
                frames.Add(Packet(id, 0x03, 0, 0, 0, unchecked((byte)wheel.Delta), wheel.Modifier));
                break;
        }
        frames.Add(WriteFlash());
        return frames;
    }

    static byte[] Packet(params byte[] bytes)
    {
        var packet = new byte[K8890Protocol.PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }
}
