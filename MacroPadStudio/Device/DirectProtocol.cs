using OshiMakiKey.Model;

namespace OshiMakiKey.Device;

/// <summary>
/// «Прямой» протокол платы CH552 1189:8890 с битовым мультимедиа-интерфейсом (6 клавиш + ручка).
/// Прошивка не разбирает поля: первый байт кадра — номер кнопки, следующие 8 байт она хранит как готовый
/// HID-отчёт клавиатуры [модификаторы][0][код1…код6] и отправляет его при нажатии. Поэтому сама она умеет
/// только одно сочетание клавиш на кнопку — остальное делает фоновый агент через служебные клавиши.
///   кнопка   [номер][модификаторы][00][код1…код6]
///   запись   [AA][AA]   (как у официальной программы)
/// Установлено пошаговой диагностикой: кадры с префиксом 0x03 эта прошивка сохраняет в кнопку 3.
/// </summary>
public static class DirectProtocol
{
    public const int MaxKeysInReport = 6;

    /// <summary>Сохраняет записанное во флеш.</summary>
    public static byte[] Close() => Packet(0xAA, 0xAA);

    public static byte[] Bind(byte slot, byte modifiers, IReadOnlyList<byte> keys)
    {
        if (keys.Count > MaxKeysInReport)
            throw new ArgumentException($"Не больше {MaxKeysInReport} клавиш одновременно.", nameof(keys));

        var packet = Packet(slot, modifiers, 0x00);
        for (int i = 0; i < keys.Count; i++) packet[3 + i] = keys[i];
        return packet;
    }

    static byte[] Packet(params byte[] bytes)
    {
        var packet = new byte[K8890Protocol.PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }
}
