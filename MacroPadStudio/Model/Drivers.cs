using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>Раскладка платы: клавиши сеткой rows × columns и ручки справа от неё.</summary>
public sealed class DriverLayout
{
    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 3;
    public int Knobs { get; set; } = 1;

    [JsonIgnore]
    public int Keys => Rows * Columns;
}

/// <summary>Цвета значка платы: корпус, клавиши, акцент (ручки).</summary>
public sealed class DriverIconColors
{
    public string Body { get; set; } = "#2B2F36";
    public string Keys { get; set; } = "#F3F4F6";
    public string Accent { get; set; } = "#3B82F6";
}

/// <summary>
/// «Драйвер» платы — её описание: USB ID, формат записи, раскладка и номера кнопок в прошивке. Сами форматы кадров
/// реализованы в программе (Device/*Protocol.cs), а файл drivers/*.json говорит, какой из них нужен этой плате.
/// </summary>
public sealed class DriverDef
{
    public string Id { get; set; } = "";

    /// <summary>Название по кодам языков: {"ru": "…", "en": "…"}.</summary>
    public Dictionary<string, string> Name { get; set; } = new();

    /// <summary>USB ID плат: «1189:8890» (VID:PID, шестнадцатеричные).</summary>
    public List<string> Usb { get; set; } = new() { "1189:8890" };

    /// <summary>true — у платы битовый мультимедиа-интерфейс, false — 16-битный код, null — не проверять.</summary>
    public bool? ConsumerBitfield { get; set; }

    /// <summary>Выбирать этот драйвер автоматически при подключении платы.</summary>
    public bool AutoDetect { get; set; } = true;

    public ProtocolMode Protocol { get; set; } = ProtocolMode.Direct;

    public DriverLayout Layout { get; set; } = new();

    /// <summary>Номера кнопок в прошивке для клавиш по порядку; не задано — 1, 2, 3…</summary>
    public List<int>? KeySlots { get; set; }

    /// <summary>Номера для ручек: по три на ручку (влево, нажатие, вправо); не задано — по формату записи.</summary>
    public List<int>? KnobSlots { get; set; }

    public DriverIconColors Icon { get; set; } = new();

    /// <summary>Пояснение по кодам языков.</summary>
    public Dictionary<string, string>? Notes { get; set; }

    [JsonIgnore]
    public string Title => L.Pick(Name, Id);

    [JsonIgnore]
    public string? Note => Notes == null ? null : L.Pick(Notes, "");

    /// <summary>Разобранные USB ID; неверные записи пропускаются.</summary>
    [JsonIgnore]
    public IReadOnlyList<(ushort Vid, ushort Pid)> UsbIds =>
        (Usb ?? new()).Select(ParseUsb).Where(p => p.Vid != 0 && p.Pid != 0).ToList();

    [JsonIgnore]
    public IReadOnlyList<ControlId> Controls => _controls ??= Model.Controls.Of(Layout.Keys, Layout.Knobs);
    List<ControlId>? _controls;

    /// <summary>Первый номер кнопки ручек в каждом формате: дальше по три на ручку.</summary>
    public static int FirstKnobSlot(ProtocolMode protocol) => protocol switch
    {
        ProtocolMode.Direct => 11,
        ProtocolMode.K884x => 16,
        ProtocolMode.K8850 => 17,
        _ => 13,
    };

    /// <summary>Номер кнопки в прошивке.</summary>
    public byte SlotOf(ControlId control)
    {
        if (Model.Controls.IsKey(control))
        {
            int key = Model.Controls.KeyIndex(control);
            return (byte)(KeySlots != null && key < KeySlots.Count ? KeySlots[key] : key + 1);
        }
        int index = Model.Controls.KnobIndex(control) * 3 + Model.Controls.KnobAction(control);
        return (byte)(KnobSlots != null && index < KnobSlots.Count ? KnobSlots[index] : FirstKnobSlot(Protocol) + index);
    }

    static (ushort Vid, ushort Pid) ParseUsb(string? text)
    {
        var parts = (text ?? "").Split(':');
        return parts.Length == 2 ? (ParseHex(parts[0]), ParseHex(parts[1])) : ((ushort)0, (ushort)0);
    }

    static ushort ParseHex(string? value) =>
        ushort.TryParse((value ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var number) ? number : (ushort)0;

    /// <summary>Пустая строка — описание корректно, иначе — что не так.</summary>
    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) return "id";
        if (UsbIds.Count == 0) return "usb";
        if (Protocol == ProtocolMode.Auto) return "protocol";
        if (Layout is null || Layout.Rows < 1 || Layout.Columns < 1 || Layout.Keys > Model.Controls.MaxKeys ||
            Layout.Knobs < 0 || Layout.Knobs > Model.Controls.MaxKnobs)
            return "layout";
        if (KeySlots != null && (KeySlots.Count != Layout.Keys || KeySlots.Any(s => s is < 1 or > 0x9F))) return "keySlots";
        if (KnobSlots != null && (KnobSlots.Count != Layout.Knobs * 3 || KnobSlots.Any(s => s is < 1 or > 0x9F))) return "knobSlots";
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
                driver.Name ??= new();
                driver.Icon ??= new();
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

    /// <summary>Драйверы, подходящие плате с таким USB ID.</summary>
    public static List<DriverDef> ForUsb(ushort vid, ushort pid) => _all.Where(d => d.UsbIds.Contains((vid, pid))).ToList();

    /// <summary>Подходящий драйвер для подключённой платы.</summary>
    public static DriverDef? Match(ushort vid, ushort pid, bool consumerBitfield)
    {
        var candidates = ForUsb(vid, pid).Where(d => d.AutoDetect).ToList();
        return candidates.FirstOrDefault(d => d.ConsumerBitfield == consumerBitfield)
               ?? candidates.FirstOrDefault(d => d.ConsumerBitfield == null);
    }

    /// <summary>Все USB ID, которые знают драйверы.</summary>
    public static IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds => _all.SelectMany(d => d.UsbIds).Distinct().ToList();

    /// <summary>Имя устройства Windows (\\?\HID#VID_1189&amp;PID_8890…) принадлежит макро-клавиатуре.</summary>
    public static bool IsPadDeviceName(string name) =>
        SupportedIds.Any(p => name.Contains($"VID_{p.Vid:X4}&PID_{p.Pid:X4}", StringComparison.OrdinalIgnoreCase));

    public static string Serialize(DriverDef driver) => JsonSerializer.Serialize(driver, Json);

    /// <summary>Встроенные драйверы — основные из тех, что поставляются в папке drivers.</summary>
    public static List<DriverDef> BuiltIns() => new()
    {
        new DriverDef
        {
            Id = "ch552-6keys-knob",
            Name = new() { ["ru"] = "CH552 · 6 клавиш + ручка", ["en"] = "CH552 · 6 keys + knob" },
            ConsumerBitfield = true,
            Protocol = ProtocolMode.Direct,
            Icon = new() { Body = "#2B2F36", Keys = "#F3F4F6", Accent = "#3B82F6" },
            Notes = new()
            {
                ["ru"] = "Прямой формат: плата сама хранит одно сочетание клавиш на кнопку, остальное выполняет программа.",
                ["en"] = "Direct format: the board stores one key combination per button, the program does the rest.",
            },
        },
        new DriverDef
        {
            Id = "ch57x-k8890",
            Name = new() { ["ru"] = "CH57x · 6 клавиш + ручка (k8890)", ["en"] = "CH57x · 6 keys + knob (k8890)" },
            ConsumerBitfield = false,
            Protocol = ProtocolMode.Legacy,
            Icon = new() { Body = "#1F2023", Keys = "#D6D3D1", Accent = "#F97316" },
            Notes = new()
            {
                ["ru"] = "Формат k8890 (ch57x-keyboard-tool): клавиши, мультимедиа и мышь хранит сама плата, есть слои.",
                ["en"] = "k8890 format (ch57x-keyboard-tool): keys, media and mouse are stored on the board, layers supported.",
            },
        },
        new DriverDef
        {
            Id = "mini-keyboard-v0",
            Name = new() { ["ru"] = "Mini Keyboard · 6 клавиш + ручка (v0)", ["en"] = "Mini Keyboard · 6 keys + knob (v0)" },
            AutoDetect = false,
            Protocol = ProtocolMode.Vendor,
            Icon = new() { Body = "#9CA3AF", Keys = "#FFFFFF", Accent = "#6366F1" },
            Notes = new()
            {
                ["ru"] = "«Версия 0» официальной программы для плат без Report ID. Только вручную.",
                ["en"] = "Official software “version 0” for boards without a report ID. Manual choice only.",
            },
        },
        new DriverDef
        {
            Id = "ch57x-k884x-12keys-3knobs",
            Name = new() { ["ru"] = "CH57x · 12 клавиш + 3 ручки (k884x)", ["en"] = "CH57x · 12 keys + 3 knobs (k884x)" },
            Usb = new() { "1189:8840", "1189:8842", "1189:8850", "514C:8851", "1189:8830", "1189:8831", "1189:8832", "1189:8833", "1189:8810" },
            Protocol = ProtocolMode.K884x,
            Layout = new() { Rows = 3, Columns = 4, Knobs = 3 },
            Icon = new() { Body = "#1E293B", Keys = "#E2E8F0", Accent = "#22D3EE" },
            Notes = new()
            {
                ["ru"] = "Формат k884x (ch57x-keyboard-tool): клавиши, мультимедиа и мышь хранит сама плата, есть слои.",
                ["en"] = "k884x format (ch57x-keyboard-tool): keys, media and mouse are stored on the board, layers supported.",
            },
        },
    };
}
