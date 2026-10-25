using System.Text;
using System.Text.Json;
using OshiMakiKey.Device;
using OshiMakiKey.Model;

namespace OshiMakiKey;

/// <summary>Проверки без интерфейса: --selftest (протокол и модель) и --probe (поиск устройства).</summary>
internal static class SelfTest
{
    public static int Run(string reportPath)
    {
        var log = new StringBuilder();
        int failures = 0;

        void Check(string name, bool ok, string details = "")
        {
            log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || details.Length == 0 ? "" : "   -> " + details)}");
            if (!ok) failures++;
        }

        void Packets(string name, List<byte[]> actual, params string[] expected)
        {
            var hex = actual.Select(Hex).ToList();
            Check(name, hex.SequenceEqual(expected), string.Join(" | ", hex));
        }

        // Эталонные байты взяты из тестов kriomant/ch57x-keyboard-tool (модель 1189:8890).
        Packets("Ctrl+A на клавише 1", K8890Protocol.Bind(0, ControlId.Key1, new KeysAction(new[] { ((byte)0x01, (byte)0x04) })),
            "03 fe 01 01 01", "03 01 11 01", "03 01 11 01 01 01 04", "03 aa aa");
        Packets("Громкость+ на клавише 2", K8890Protocol.Bind(0, ControlId.Key2, new MediaAction(0xE9)),
            "03 fe 01 01 01", "03 02 12 e9", "03 aa aa");
        Packets("ЛКМ на клавише 3", K8890Protocol.Bind(0, ControlId.Key3, new MouseClickAction(1, 0)),
            "03 fe 01 01 01", "03 03 13 01", "03 aa aa");
        Packets("Колесо +3 на клавише 4", K8890Protocol.Bind(0, ControlId.Key4, new MouseWheelAction(3, 0)),
            "03 fe 01 01 01", "03 04 13 00 00 00 03", "03 aa aa");
        Packets("Колесо -1 с Ctrl на повороте влево", K8890Protocol.Bind(0, ControlId.KnobLeft, new MouseWheelAction(-1, 1)),
            "03 fe 01 01 01", "03 0d 13 00 00 00 ff 01", "03 aa aa");
        Packets("Пусто на нажатии ручки, слой 2", K8890Protocol.Bind(1, ControlId.KnobPress, new KeysAction(Array.Empty<(byte, byte)>())),
            "03 fe 02 01 01", "03 0e 21", "03 aa aa");
        Packets("Ctrl+C, Ctrl+V на повороте вправо",
            K8890Protocol.Bind(0, ControlId.KnobRight, new KeysAction(new[] { ((byte)1, KeyCatalog.C), ((byte)1, KeyCatalog.V) })),
            "03 fe 01 01 01", "03 0f 11 02", "03 0f 11 02 01 01 06", "03 0f 11 02 02 01 19", "03 aa aa");
        Packets("Пред. трек на клавише 6", K8890Protocol.Bind(0, ControlId.Key6, new MediaAction(0xB6)),
            "03 fe 01 01 01", "03 06 12 b6", "03 aa aa");
        Check("Все пакеты по 64 байта", K8890Protocol.Bind(0, ControlId.Key1, new MediaAction(0xCD)).All(p => p.Length == 64));

        Check("Каталог: HID-коды уникальны", KeyCatalog.All.Select(k => k.Code).Distinct().Count() == KeyCatalog.All.Count);
        Check("Клавиша A → 0x04", KeyCatalog.FromVirtualKey(Keys.A) == 0x04);
        Check("Клавиша Z → 0x1D", KeyCatalog.FromVirtualKey(Keys.Z) == 0x1D);
        Check("Клавиша 0 → 0x27", KeyCatalog.FromVirtualKey(Keys.D0) == 0x27);
        Check("Enter → 0x28", KeyCatalog.FromVirtualKey(Keys.Enter) == 0x28);
        Check("F13 → 0x68", KeyCatalog.FromVirtualKey(Keys.F13) == 0x68);
        Check("Запятая (Б) → 0x36", KeyCatalog.FromVirtualKey(Keys.Oemcomma) == 0x36);
        Check("Стрелка вверх → 0x52", KeyCatalog.FromVirtualKey(Keys.Up) == 0x52);
        Check("Название Ctrl+Shift+Esc", BindingText.Chord(KeyChord.Of(0x29, ctrl: true, shift: true)) == "Ctrl + Shift + Esc");

        // Служебные клавиши для системных действий.
        var profile = Profile.CreateDefault();
        foreach (var id in Enum.GetValues<ControlId>())
            profile.Layers[0].Actions[id] = new PadAction { Kind = ActionKind.System, SystemAction = SystemActionKind.LockPc };
        profile.Layers[1].Actions[ControlId.Key1] = PadAction.FromKeys(KeyChord.Of(0x68)); // пользователь сам назначил F13
        profile.Layers[1].Actions[ControlId.Key2] = new PadAction { Kind = ActionKind.System, SystemAction = SystemActionKind.Sleep };
        bool SystemOnly(PadAction a) => a.Kind == ActionKind.System;
        profile.EnsureTriggers(SystemOnly);
        var slots = profile.All(Profile.MaxLayers).Select(x => x.Action.TriggerSlot).OfType<int>().ToList();
        int expectedSlots = Enum.GetValues<ControlId>().Length + 1;
        Check("Служебные клавиши уникальны", slots.Count == expectedSlots && slots.Distinct().Count() == expectedSlots, string.Join(",", slots));
        Check("F13 не занят, раз он назначен пользователем", !slots.Contains(0), string.Join(",", slots));
        int before = profile.Layers[0].Get(ControlId.Key3).TriggerSlot ?? -1;
        profile.Layers[0].Get(ControlId.Key3).Target = "изменили параметр";
        profile.EnsureTriggers(SystemOnly);
        Check("Номер служебной клавиши не меняется при правке параметров", profile.Layers[0].Get(ControlId.Key3).TriggerSlot == before);
        Check("Служебная клавиша 13 = Shift + F14", TriggerKeys.Describe(13) == "Shift + F14", TriggerKeys.Describe(13));
        var systemPackets = ProfileCompiler.ToHardware(new PadAction { Kind = ActionKind.System, TriggerSlot = 25 });
        Check("Системное действие → Ctrl + F14", systemPackets is KeysAction { Presses: [(0x01, 0x69)] });

        // Прямой протокол (новая ревизия): кадр = [03][кнопка][модификаторы][00][коды…], обрамление A1 … AA AA.
        Packets("Прямой: Ctrl+C на клавише 1", new List<byte[]> { DirectProtocol.Bind(ControlId.Key1, 0x01, new[] { KeyCatalog.C }) },
            "03 01 01 00 06");
        Packets("Прямой: пустая кнопка", new List<byte[]> { DirectProtocol.Bind(ControlId.KnobPress, 0, Array.Empty<byte>()) }, "03 0e");
        Packets("Прямой: открыть и сохранить", new List<byte[]> { DirectProtocol.Open(), DirectProtocol.Close() }, "03 a1 01", "03 aa aa");

        var direct = new AppSettings { Protocol = ProtocolMode.Direct };
        direct.Normalize();
        // Пример профиля: клавиши 1–3 — сочетания (их хранит само устройство), клавиши 4–6 и ручка — мультимедиа (через агента).
        Packets("Прямой: пример профиля целиком", ProfileCompiler.Compile(direct),
            "03 a1 01",
            "03 01 01 00 06", "03 02 01 00 19", "03 03 01 00 1d",
            "03 04 00 00 68", "03 05 00 00 69", "03 06 00 00 6a",
            "03 0d 00 00 6b", "03 0e 00 00 6c", "03 0f 00 00 6d",
            "03 aa aa");
        Check("Прямой: без пустого кадра в начале", !ProfileCompiler.SendsInitPacket(ProtocolMode.Direct));
        Check("Прямой: одно сочетание — само устройство",
            !Execution.NeedsAgent(PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true)), ProtocolMode.Direct));
        Check("Прямой: последовательность — через агента",
            Execution.NeedsAgent(PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true), KeyChord.Of(KeyCatalog.V, ctrl: true)), ProtocolMode.Direct));
        Check("Прямой: мультимедиа — через агента", Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Direct));
        Check("ch57x: мультимедиа — само устройство", !Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Legacy));
        Check("Прямой: слоёв всегда один", new AppSettings { Protocol = ProtocolMode.Direct, LayerCount = 3 }.EffectiveLayerCount == 1);

        // Формат официальной программы («версия 0»): без префикса 0x03, запись во флеш после каждой кнопки.
        Packets("Офиц.: Ctrl+C на клавише 1", VendorProtocol.Bind(ControlId.Key1, new KeysAction(new[] { ((byte)0x01, KeyCatalog.C) })),
            "01 01 01 00 01", "01 01 01 01 01 06", "aa aa");
        Packets("Офиц.: громкость+ на клавише 2 (бит 1)", VendorProtocol.Bind(ControlId.Key2, new MediaAction(MediaCatalog.VolumeUp)),
            "02 02 02", "aa aa");
        Packets("Офиц.: пауза на клавише 3 (бит 5)", VendorProtocol.Bind(ControlId.Key3, new MediaAction(MediaCatalog.PlayPause)),
            "03 02 20", "aa aa");
        Packets("Офиц.: пустое нажатие ручки", VendorProtocol.Bind(ControlId.KnobPress, new KeysAction(Array.Empty<(byte, byte)>())),
            "0e 01 01", "0e 01 01 01", "aa aa");
        Check("Офиц.: калькулятора нет в отчёте платы — через агента",
            Execution.NeedsAgent(PadAction.FromMedia(0x192), ProtocolMode.Vendor));
        Check("Офиц.: громкость есть в отчёте платы — само устройство",
            !Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Vendor));

        // Сохранение и загрузка.
        var settings = new AppSettings { Protocol = ProtocolMode.Legacy };
        settings.Normalize();
        settings.Profile.Layers[0].Actions[ControlId.Key2] = new PadAction { Kind = ActionKind.Mouse, Mouse = MouseActionKind.WheelDown, WheelStep = 3, MouseModifier = MouseModifier.Ctrl };
        settings.EnsureTriggers();
        string json = JsonSerializer.Serialize(settings, SettingsStore.Json);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.Json)!;
        restored.Normalize();
        Check("JSON: конфигурация совпадает после сохранения",
            ProfileCompiler.Signature(ProfileCompiler.Compile(settings)) == ProfileCompiler.Signature(ProfileCompiler.Compile(restored)));
        int controls = Enum.GetValues<ControlId>().Length;
        int compiled = ProfileCompiler.Compile(settings).Count;
        Check($"ch57x: пример профиля — {controls} действий × 3–4 пакета", compiled >= 3 * controls && compiled <= 4 * controls, compiled.ToString());

        log.AppendLine();
        log.AppendLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        File.WriteAllText(reportPath, log.ToString());
        return failures == 0 ? 0 : 1;
    }

    public static int Probe(string reportPath)
    {
        var log = new StringBuilder();
        int code = 0;
        foreach (var (vid, pid) in MacroPadDevice.Supported)
            foreach (var d in HidEnumerator.Enumerate(vid, pid))
                log.AppendLine($"{d.VendorId:X4}:{d.ProductId:X4} page=0x{d.UsagePage:X4} usage=0x{d.Usage:X4} in={d.InputReportLength} out={d.OutputReportLength} valueCaps={d.InputValueCaps}  {d.Path}");
        log.AppendLine("Определённый способ записи: " + (MacroPadDevice.DetectProtocol()?.ToString() ?? "—"));

        var info = MacroPadDevice.FindConfigInterface();
        if (info == null)
        {
            log.AppendLine("Служебный интерфейс: НЕ НАЙДЕН");
            code = 2;
        }
        else
        {
            log.AppendLine("Служебный интерфейс: " + info.Path);
            try
            {
                using var device = MacroPadDevice.Open(info);
                device.SendInit();
                log.AppendLine("Открытие и пустой пакет инициализации: OK");
            }
            catch (Exception ex)
            {
                log.AppendLine("ОШИБКА: " + ex.Message);
                code = 3;
            }
        }
        File.WriteAllText(reportPath, log.ToString());
        return code;
    }

    static string Hex(byte[] packet)
    {
        int end = packet.Length;
        while (end > 0 && packet[end - 1] == 0) end--;
        return string.Join(" ", packet.Take(end).Select(b => b.ToString("x2")));
    }
}
