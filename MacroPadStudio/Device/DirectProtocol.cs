using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>
/// «Прямой» протокол ревизии CH552 1189:8890 с битовым мультимедиа-интерфейсом (6 клавиш + ручка).
/// Прошивка не разбирает поля: 8 байт после номера кнопки она хранит как готовый HID-отчёт клавиатуры
/// [модификаторы][0][код1…код6] и отправляет его при нажатии. Поэтому сама она умеет только одно
/// сочетание клавиш на кнопку — остальное делает фоновый агент через служебные клавиши.
/// Формат измерен проектом barkleesanders/padclaude. Кадры идут тем же interrupt OUT, что и у ch57x
/// (SET_REPORT прошивка отклоняет).
/// </summary>
public static class DirectProtocol
{
    public const int MaxKeysInReport = 6;

    /// <summary>Открывает сеанс записи (иначе прошивка не сохраняет кадры).</summary>
    public static byte[] Open() => Packet(0x03, 0xA1, 0x01);

    /// <summary>Сохраняет записанное во флеш.</summary>
    public static byte[] Close() => Packet(0x03, 0xAA, 0xAA);

    public static byte[] Bind(ControlId control, byte modifiers, IReadOnlyList<byte> keys)
    {
        if (keys.Count > MaxKeysInReport)
            throw new ArgumentException($"Не больше {MaxKeysInReport} клавиш одновременно.", nameof(keys));

        var packet = Packet(0x03, K8890Protocol.KeyId(control), modifiers, 0x00);
        for (int i = 0; i < keys.Count; i++) packet[4 + i] = keys[i];
        return packet;
    }

    static byte[] Packet(params byte[] bytes)
    {
        var packet = new byte[K8890Protocol.PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }
}
