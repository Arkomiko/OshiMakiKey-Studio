using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>
/// «Драйвер» платы — её описание: USB ID, формат записи и номера кнопок в прошивке. Сами форматы кадров
/// реализованы в программе (Device/*Protocol.cs), а файл drivers/*.json говорит, какой из них нужен этой плате.
/// </summary>
public sealed class DriverDef
{
    public string Id { get; set; } = "";

    /// <summary>Название по кодам языков: {"ru": "…", "en": "…"}.</summary>
    public Dictionary<string, string> Name { get; set; } = new();

    public string VendorId { get; set; } = "1189";
    public string ProductId { get; set; } = "8890";

    /// <summary>true — у платы битовый мультимедиа-интерфейс, false — 16-битный код, null — не проверять.</summary>
    public bool? ConsumerBitfield { get; set; }

    /// <summary>Выбирать этот драйвер автоматически при подключении платы.</summary>
    public bool AutoDetect { get; set; } = true;

    public ProtocolMode Protocol { get; set; } = ProtocolMode.Direct;

    /// <summary>Номера кнопок в прошивке для клавиш 1–6.</summary>
    public List<int> KeySlots { get; set; } = new() { 1, 2, 3, 4, 5, 6 };

    /// <summary>Номера кнопок в прошивке для ручки: поворот влево, нажатие, поворот вправо.</summary>
    public List<int> KnobSlots { get; set; } = new() { 11, 12, 13 };

    public string? Notes { get; set; }

    [JsonIgnore] public ushort Vid => ParseHex(VendorId);
    [JsonIgnore] public ushort Pid => ParseHex(ProductId);

    [JsonIgnore]
    public string Title => L.Pick(Name, Id);

    static ushort ParseHex(string? value) =>
        ushort.TryParse((value ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var number) ? number : (ushort)0;

    /// <summary>Пустая строка — описание корректно, иначе — что не так.</summary>
    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) return "id";
        if (Vid == 0 || Pid == 0) return "vendorId / productId";
        if (Protocol == ProtocolMode.Auto) return "protocol";
        if (KeySlots is not { Count: 6 } || KeySlots.Any(s => s is < 1 or > 0x9F)) return "keySlots (6)";
        if (KnobSlots is not { Count: 3 } || KnobSlots.Any(s => s is < 1 or > 0x9F)) return "knobSlots (3)";
        return "";
    }
}

public static class DriverStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    static List<DriverDef> _all = BuiltIns();

    public static IReadOnlyList<DriverDef> All => _all;

    /// <summary>Драйверы из папки drivers; если их нет — встроенные (и уведомление).</summary>
    public static void Load()
    {
        var loaded = new List<DriverDef>();
        foreach (var file in AppPaths.JsonFiles(AppPaths.Drivers))
        {
            try
            {
                var driver = JsonSerializer.Deserialize<DriverDef>(File.ReadAllText(file), Json) ?? throw new JsonException("empty");
                string problem = driver.Validate();
                if (problem.Length > 0) throw new JsonException(problem);
                if (loaded.All(d => d.Id != driver.Id)) loaded.Add(driver);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Notices.AddBadFile(file, ex.Message);
            }
        }

        if (loaded.Count == 0)
        {
            Notices.AddMissing(L.T("notice.missing.drivers", "драйверов"));
            loaded = BuiltIns();
        }
        _all = loaded;
    }

    public static DriverDef? Find(string? id) => id == null ? null : _all.FirstOrDefault(d => d.Id == id);

    /// <summary>Драйвер по умолчанию, пока плата не определена.</summary>
    public static DriverDef Default => _all.FirstOrDefault(d => d.AutoDetect) ?? _all[0];

    public static DriverDef? FirstWith(ProtocolMode protocol) => _all.FirstOrDefault(d => d.Protocol == protocol);

    /// <summary>Подходящий драйвер для подключённой платы.</summary>
    public static DriverDef? Match(ushort vid, ushort pid, bool consumerBitfield) =>
        _all.FirstOrDefault(d => d.AutoDetect && d.Vid == vid && d.Pid == pid && d.ConsumerBitfield == consumerBitfield)
        ?? _all.FirstOrDefault(d => d.AutoDetect && d.Vid == vid && d.Pid == pid && d.ConsumerBitfield == null);

    /// <summary>Все USB ID, которые знают драйверы.</summary>
    public static IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds =>
        _all.Select(d => (d.Vid, d.Pid)).Where(p => p.Vid != 0).Distinct().ToList();

    /// <summary>Имя устройства Windows (\\?\HID#VID_1189&amp;PID_8890…) принадлежит макро-клавиатуре.</summary>
    public static bool IsPadDeviceName(string name) =>
        SupportedIds.Any(p => name.Contains($"VID_{p.Vid:X4}&PID_{p.Pid:X4}", StringComparison.OrdinalIgnoreCase));

    public static string Serialize(DriverDef driver) => JsonSerializer.Serialize(driver, Json);

    /// <summary>Встроенные драйверы — те же, что поставляются в папке drivers.</summary>
    public static List<DriverDef> BuiltIns() => new()
    {
        new DriverDef
        {
            Id = "ch552-6keys-knob",
            Name = new() { ["ru"] = "CH552 · 6 клавиш + ручка", ["en"] = "CH552 · 6 keys + knob" },
            ConsumerBitfield = true,
            Protocol = ProtocolMode.Direct,
            KnobSlots = new() { 11, 12, 13 },
            Notes = "Прямой формат: [кнопка][HID-отчёт клавиатуры], в конце AA AA. Сама плата хранит одно сочетание " +
                    "клавиш на кнопку; мультимедиа, мышь и последовательности выполняет программа.",
        },
        new DriverDef
        {
            Id = "ch57x-k8890",
            Name = new() { ["ru"] = "CH57x · формат ch57x-keyboard-tool", ["en"] = "CH57x · ch57x-keyboard-tool format" },
            ConsumerBitfield = false,
            Protocol = ProtocolMode.Legacy,
            KnobSlots = new() { 13, 14, 15 },
            Notes = "Формат k8890 из kriomant/ch57x-keyboard-tool: прошивка сама разбирает клавиши, мультимедиа и мышь, есть слои.",
        },
        new DriverDef
        {
            Id = "mini-keyboard-v0",
            Name = new() { ["ru"] = "Mini Keyboard · формат официальной программы", ["en"] = "Mini Keyboard · official software format" },
            ConsumerBitfield = null,
            AutoDetect = false,
            Protocol = ProtocolMode.Vendor,
            KnobSlots = new() { 13, 14, 15 },
            Notes = "«Версия 0» официальной программы для плат без Report ID (по коду rOzzy1987/MacroPad). Только вручную.",
        },
    };
}
