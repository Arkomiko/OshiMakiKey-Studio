using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OshiMakiKey.Device;
using OshiMakiKey.Model;
using OshiMakiKey.UI;

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
        Packets("Ctrl+A на клавише 1", K8890Protocol.Bind(0, (byte)1, new KeysAction(new[] { ((byte)0x01, (byte)0x04) })),
            "03 fe 01 01 01", "03 01 11 01", "03 01 11 01 01 01 04", "03 aa aa");
        Packets("Громкость+ на клавише 2", K8890Protocol.Bind(0, (byte)2, new MediaAction(0xE9)),
            "03 fe 01 01 01", "03 02 12 e9", "03 aa aa");
        Packets("ЛКМ на клавише 3", K8890Protocol.Bind(0, (byte)3, new MouseClickAction(1, 0)),
            "03 fe 01 01 01", "03 03 13 01", "03 aa aa");
        Packets("Колесо +3 на клавише 4", K8890Protocol.Bind(0, (byte)4, new MouseWheelAction(3, 0)),
            "03 fe 01 01 01", "03 04 13 00 00 00 03", "03 aa aa");
        Packets("Колесо -1 с Ctrl на повороте влево", K8890Protocol.Bind(0, (byte)13, new MouseWheelAction(-1, 1)),
            "03 fe 01 01 01", "03 0d 13 00 00 00 ff 01", "03 aa aa");
        Packets("Пусто на нажатии ручки, слой 2", K8890Protocol.Bind(1, (byte)14, new KeysAction(Array.Empty<(byte, byte)>())),
            "03 fe 02 01 01", "03 0e 21", "03 aa aa");
        Packets("Ctrl+C, Ctrl+V на повороте вправо",
            K8890Protocol.Bind(0, (byte)15, new KeysAction(new[] { ((byte)1, KeyCatalog.C), ((byte)1, KeyCatalog.V) })),
            "03 fe 01 01 01", "03 0f 11 02", "03 0f 11 02 01 01 06", "03 0f 11 02 02 01 19", "03 aa aa");
        Packets("Пред. трек на клавише 6", K8890Protocol.Bind(0, (byte)6, new MediaAction(0xB6)),
            "03 fe 01 01 01", "03 06 12 b6", "03 aa aa");
        Check("Все пакеты по 64 байта", K8890Protocol.Bind(0, (byte)1, new MediaAction(0xCD)).All(p => p.Length == 64));

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
        var sixKeys = Controls.Of(6, 1);
        foreach (var id in sixKeys)
            profile.Layers[0].Actions[id] = new PadAction { Kind = ActionKind.System, SystemAction = SystemActionKind.LockPc };
        profile.Layers[1].Actions[ControlId.Key1] = PadAction.FromKeys(KeyChord.Of(0x68)); // пользователь сам назначил F13
        profile.Layers[1].Actions[ControlId.Key2] = new PadAction { Kind = ActionKind.System, SystemAction = SystemActionKind.Sleep };
        bool SystemOnly(PadAction a) => a.Kind == ActionKind.System;
        profile.EnsureTriggers(SystemOnly, sixKeys);
        var slots = profile.All(Profile.MaxLayers, sixKeys).Select(x => x.Action.TriggerSlot).OfType<int>().ToList();
        int expectedSlots = sixKeys.Count + 1;
        Check("Служебные клавиши уникальны", slots.Count == expectedSlots && slots.Distinct().Count() == expectedSlots, string.Join(",", slots));
        Check("F13 не занят, раз он назначен пользователем", !slots.Contains(0), string.Join(",", slots));
        int before = profile.Layers[0].Get(ControlId.Key3).TriggerSlot ?? -1;
        profile.Layers[0].Get(ControlId.Key3).Target = "изменили параметр";
        profile.EnsureTriggers(SystemOnly, sixKeys);
        Check("Номер служебной клавиши не меняется при правке параметров", profile.Layers[0].Get(ControlId.Key3).TriggerSlot == before);
        Check("Служебная клавиша 13 = Shift + F14", TriggerKeys.Describe(13) == "Shift + F14", TriggerKeys.Describe(13));
        var systemPackets = ProfileCompiler.ToHardware(new PadAction { Kind = ActionKind.System, TriggerSlot = 25 });
        Check("Системное действие → Ctrl + F14", systemPackets is KeysAction { Presses: [(0x01, 0x69)] });

        // Прямой протокол: кадр = [кнопка][модификаторы][00][коды…] (готовый HID-отчёт), в конце AA AA.
        Packets("Прямой: Ctrl+C на клавише 1", new List<byte[]> { DirectProtocol.Bind(1, 0x01, new[] { KeyCatalog.C }) },
            "01 01 00 06");
        Packets("Прямой: пустая кнопка", new List<byte[]> { DirectProtocol.Bind(14, 0, Array.Empty<byte>()) }, "0e");
        Packets("Прямой: сохранить", new List<byte[]> { DirectProtocol.Close() }, "aa aa");

        var direct = new AppSettings { Protocol = ProtocolMode.Direct };
        direct.Normalize();
        // Пример профиля: клавиши 1–3 — сочетания (их хранит само устройство), клавиши 4–6 и ручка — мультимедиа (через агента).
        Packets("Прямой: пример профиля целиком", ProfileCompiler.Compile(direct),
            "01 01 00 06", "02 01 00 19", "03 01 00 1d",
            "04 00 00 68", "05 00 00 69", "06 00 00 6a",
            "0b 00 00 6b", "0c 00 00 6c", "0d 00 00 6d",
            "aa aa");
        direct.LearnedKnobSlots = new() { [direct.EffectiveDriver.Id] = new List<int> { 16, 17, 15 } };
        Check("Прямой: найденные номера ручки используются при записи",
            ProfileCompiler.Compile(direct)[6][0] == 16 && ProfileCompiler.Compile(direct)[7][0] == 17 && ProfileCompiler.Compile(direct)[8][0] == 15);
        Check("Прямой: без пустого кадра в начале", !ProfileCompiler.SendsInitPacket(ProtocolMode.Direct));
        Check("Прямой: одно сочетание — само устройство",
            !Execution.NeedsAgent(PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true)), ProtocolMode.Direct));
        Check("Прямой: последовательность — через агента",
            Execution.NeedsAgent(PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true), KeyChord.Of(KeyCatalog.V, ctrl: true)), ProtocolMode.Direct));
        Check("Прямой: мультимедиа — через агента", Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Direct));
        Check("ch57x: мультимедиа — само устройство", !Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Legacy));
        var layered = new AppSettings { Protocol = ProtocolMode.Direct, LayerCount = 3 };
        layered.Normalize();
        Check("Прямой: слоёв всегда один", layered.EffectiveLayerCount == 1);
        var migrated = new AppSettings { Protocol = ProtocolMode.Legacy, DetectedProtocol = ProtocolMode.Direct, KnobSlots = new List<int> { 7, 8, 9 } };
        migrated.Normalize();
        Check("Перенос: способ записи прежней версии → драйвер", migrated.DriverId == "ch57x-k8890" && migrated.DetectedDriverId == "ch552-6keys-knob",
            $"{migrated.DriverId} / {migrated.DetectedDriverId}");
        Check("Перенос: номера ручки → драйвер прямого формата",
            migrated.LearnedKnobSlots?.GetValueOrDefault("ch552-6keys-knob") is [7, 8, 9] && migrated.KnobSlots == null);

        // Формат официальной программы («версия 0»): без префикса 0x03, запись во флеш после каждой кнопки.
        Packets("Офиц.: Ctrl+C на клавише 1", VendorProtocol.Bind((byte)1, new KeysAction(new[] { ((byte)0x01, KeyCatalog.C) })),
            "01 01 01 00 01", "01 01 01 01 01 06", "aa aa");
        Packets("Офиц.: громкость+ на клавише 2 (бит 1)", VendorProtocol.Bind((byte)2, new MediaAction(MediaCatalog.VolumeUp)),
            "02 02 02", "aa aa");
        Packets("Офиц.: пауза на клавише 3 (бит 5)", VendorProtocol.Bind((byte)3, new MediaAction(MediaCatalog.PlayPause)),
            "03 02 20", "aa aa");
        Packets("Офиц.: пустое нажатие ручки", VendorProtocol.Bind((byte)14, new KeysAction(Array.Empty<(byte, byte)>())),
            "0e 01 01", "0e 01 01 01", "aa aa");
        Check("Офиц.: калькулятора нет в отчёте платы — через агента",
            Execution.NeedsAgent(PadAction.FromMedia(0x192), ProtocolMode.Vendor));
        Check("Офиц.: громкость есть в отчёте платы — само устройство",
            !Execution.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp), ProtocolMode.Vendor));

        // Формат k884x (1189:8840/8842/8850, 514C:8851): эталоны из тестов ch57x-keyboard-tool, после кнопки — команды записи.
        const string k884xFinish1 = "03 aa aa", k884xFinish2 = "03 fd fe ff";
        Packets("k884x: Ctrl+A на кнопке 1", K884xProtocol.Bind(0, 1, new KeysAction(new[] { ((byte)0x01, (byte)0x04) })),
            "03 fe 01 01 01 00 00 00 00 00 01 01 04", k884xFinish1, k884xFinish2, k884xFinish1);
        Packets("k884x: громкость+ на кнопке 2", K884xProtocol.Bind(0, 2, new MediaAction(0xE9)),
            "03 fe 02 01 02 00 00 00 00 00 00 e9", k884xFinish1, k884xFinish2, k884xFinish1);
        Packets("k884x: ЛКМ на кнопке 3", K884xProtocol.Bind(0, 3, new MouseClickAction(1, 0)),
            "03 fe 03 01 03 00 00 00 00 00 01 00 01", k884xFinish1, k884xFinish2, k884xFinish1);
        Packets("k884x: колесо +3 на кнопке 5", K884xProtocol.Bind(0, 5, new MouseWheelAction(3, 0)),
            "03 fe 05 01 03 00 00 00 00 00 03 00 00 00 00 03", k884xFinish1, k884xFinish2, k884xFinish1);
        Packets("k8850: громкость+ на кнопке 1", K8850Protocol.Bind(0, 1, new MediaAction(0xE9)),
            "03 fd 01 01 02 00 02 00 00 e9", "03 fd fe ff");
        Packets("k8850: Ctrl+C — модификатор отдельной записью", K8850Protocol.Bind(0, 2, new KeysAction(new[] { ((byte)0x01, KeyCatalog.C) })),
            "03 fd 02 01 01 00 02 00 00 f1 00 00 06", "03 fd fe ff");

        // Раскладки драйверов и номера кнопок прошивки.
        var k884x = DriverStore.BuiltIns().First(d => d.Protocol == ProtocolMode.K884x);
        Check("k884x 12+3: 21 кнопка", k884x.Controls.Count == 21, k884x.Controls.Count.ToString());
        Check("k884x: клавиша 12 → 12, ручка 1 влево → 16, ручка 3 вправо → 24",
            k884x.SlotOf(ControlId.Key12) == 12 && k884x.SlotOf(ControlId.KnobLeft) == 16 && k884x.SlotOf(ControlId.Knob3Right) == 24);
        var legacyDriver = DriverStore.BuiltIns().First(d => d.Protocol == ProtocolMode.Legacy);
        Check("k8890: ручка → 13/14/15", legacyDriver.SlotOf(ControlId.KnobLeft) == 13 && legacyDriver.SlotOf(ControlId.KnobRight) == 15);
        Check("Прямой: ручка → 11/12/13", DriverStore.Default.SlotOf(ControlId.KnobLeft) == 11 && DriverStore.Default.SlotOf(ControlId.KnobRight) == 13);
        var big = new AppSettings { DriverId = k884x.Id, LayerCount = 3 };
        big.Normalize();
        Check("k884x: слои записываются", big.EffectiveLayerCount == 3);
        Check("k884x: пример профиля — 3 слоя × 21 кнопка × 4 кадра", ProfileCompiler.Compile(big).Count == 3 * 21 * 4, ProfileCompiler.Compile(big).Count.ToString());
        Check("k884x: мультимедиа — сама плата", !big.NeedsAgent(PadAction.FromMedia(MediaCatalog.VolumeUp)));
        var noRepeat = new AppSettings { NoKeyRepeat = true };
        noRepeat.Normalize();
        Check("Без автоповтора: сочетание выполняет программа", noRepeat.NeedsAgent(PadAction.FromKeys(KeyChord.Of(KeyCatalog.C, ctrl: true))));

        // Сохранение и загрузка.
        var settings = new AppSettings { Protocol = ProtocolMode.Legacy };
        settings.Normalize();
        settings.Profile.Layers[0].Actions[ControlId.Key2] = new PadAction { Kind = ActionKind.Mouse, Mouse = MouseActionKind.WheelDown, WheelStep = 3, MouseModifier = MouseModifier.Ctrl };
        settings.EnsureTriggers();
        string json = JsonSerializer.Serialize(settings, SettingsStore.Json);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.Json)!;
        restored.Profile = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(settings.Profile, SettingsStore.Json), SettingsStore.Json)!;
        restored.Normalize();
        Check("JSON: раскладка не попадает в settings.json", !json.Contains("\"Layers\""));
        Check("JSON: конфигурация совпадает после сохранения",
            ProfileCompiler.Signature(ProfileCompiler.Compile(settings)) == ProfileCompiler.Signature(ProfileCompiler.Compile(restored)));
        int controls = settings.Controls.Count;
        int compiled = ProfileCompiler.Compile(settings).Count;
        Check($"ch57x: пример профиля — {controls} действий × 3–4 пакета", compiled >= 3 * controls && compiled <= 4 * controls, compiled.ToString());

        ResourceChecks(Check);

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
        log.AppendLine("Подобранный драйвер: " + (MacroPadDevice.DetectDriver()?.Id ?? "—"));

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

    /// <summary>Файлы ресурсов рядом с программой, работа без них, пресеты.</summary>
    static void ResourceChecks(Action<string, bool, string> check)
    {
        void Check(string name, bool ok, string details = "") => check(name, ok, details);

        // Драйверы: в папке те же описания, что встроены в программу.
        Notices.Clear();
        DriverStore.Load();
        var builtIns = DriverStore.BuiltIns();
        // Названия и пояснения в файлах на всех языках, во встроенных — только ru/en; сравниваем техническую часть.
        static string Tech(DriverDef d) => $"{d.Protocol}|{string.Join(",", d.Usb)}|{d.ConsumerBitfield}|{d.AutoDetect}|{d.Layout.Rows}x{d.Layout.Columns}+{d.Layout.Knobs}|" +
                                           string.Join(",", d.Controls.Select(d.SlotOf));
        Check("drivers: все встроенные драйверы лежат в папке",
            builtIns.All(b => DriverStore.Find(b.Id) is { } d && Tech(d) == Tech(b)),
            string.Join(", ", DriverStore.All.Select(d => d.Id)));
        Check("drivers: файлы без ошибок", Notices.All.Count == 0, string.Join(" | ", Notices.All.Select(n => n.Text)));

        // Темы: 12 штук, у каждой полный и правильный набор цветов.
        Notices.Clear();
        ThemeStore.Load();
        var colorKeys = typeof(Theme).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Color))
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToHashSet();
        Check("theme: 13 светлых и 13 тёмных", ThemeStore.All.Count(t => !t.Dark) == 13 && ThemeStore.All.Count(t => t.Dark) == 13, string.Join(", ", ThemeStore.All.Select(t => t.Id)));
        Check("theme: файлы без ошибок", Notices.All.Count == 0, string.Join(" | ", Notices.All.Select(n => n.Text)));
        foreach (var theme in ThemeStore.All)
        {
            var keys = theme.Colors.Keys.ToHashSet();
            Check($"theme/{theme.Id}: все цвета заданы и известны", keys.SetEquals(colorKeys),
                "лишние: " + string.Join(",", keys.Except(colorKeys)) + "; нет: " + string.Join(",", colorKeys.Except(keys)));
            Check($"theme/{theme.Id}: названия ru и en", theme.Name.ContainsKey("ru") && theme.Name.ContainsKey("en"));
        }
        ThemeStore.Apply("нет-такой-темы");
        Check("theme: неизвестная тема → светлая и уведомление",
            ThemeStore.Current.Id == ThemeStore.DefaultId && Notices.All.Any(n => n.Text.Contains("нет-такой-темы")));

        // Языки: в английском те же ключи и те же {0}, {1}…, что в русском.
        var packs = AppPaths.JsonFiles(AppPaths.Languages).ToDictionary(f => Path.GetFileNameWithoutExtension(f), ReadStrings);
                Check("language: 9 языков", packs.Count == 9, string.Join(", ", packs.Keys));
        if (packs.TryGetValue("ru", out var ru))
        {
            // Перевод может не использовать какую-то подстановку, но не должен ссылаться на несуществующую.
            var placeholders = new Regex(@"\{(\d+)[^}]*\}");
            HashSet<string> Slots(string text) => placeholders.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var (code, pack) in packs.Where(p => p.Key != "ru"))
            {
                Check($"language/{code}: все ключи ru", ru.Keys.All(pack.ContainsKey), string.Join(", ", ru.Keys.Where(k => !pack.ContainsKey(k)).Take(10)));
                Check($"language/{code}: нет лишних ключей", pack.Keys.All(ru.ContainsKey), string.Join(", ", pack.Keys.Where(k => !ru.ContainsKey(k)).Take(10)));
                var mismatched = ru.Keys.Where(k => pack.TryGetValue(k, out var t) && !Slots(t).IsSubsetOf(Slots(ru[k]))).ToList();
                Check($"language/{code}: нет лишних подстановок {{n}}", mismatched.Count == 0, string.Join(", ", mismatched.Take(10)));
            }
        }

        // Без папок ресурсов: встроенные настройки и уведомление.
        string realRoot = AppPaths.Root;
        string temp = Path.Combine(Path.GetTempPath(), "OshiMakiKeySelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            static string Missing(string what) =>
                $"Отсутствуют файлы {what}, в данный момент применяются стандартные встроенные настройки программы. Рекомендуется проверить программу на наличие обновлений.";
            string Texts() => string.Join(" | ", Notices.All.Select(n => n.Text));

            // Удалены только темы (языки и драйверы на месте; русский пакет — чтобы текст был на русском).
            AppPaths.Root = temp;
            Directory.CreateDirectory(AppPaths.Languages);
            File.Copy(Path.Combine(realRoot, "language", "ru.json"), Path.Combine(AppPaths.Languages, "ru.json"));
            Directory.CreateDirectory(AppPaths.Drivers);
            foreach (var file in AppPaths.JsonFiles(Path.Combine(realRoot, "drivers")))
                File.Copy(file, Path.Combine(AppPaths.Drivers, Path.GetFileName(file)));
            Notices.Clear();
            L.Load("ru");
            DriverStore.Load();
            ThemeStore.Load();
            ThemeStore.Apply("dark");
            Check("Нет тем: одно уведомление «тем»", Notices.All.Count == 1 && Notices.All[0].Text == Missing("тем") && Notices.All[0].SuggestUpdate, Texts());
            Check("Нет тем: тема встроенная (светлая)", ThemeStore.Current.Id == "" && !Theme.Dark && Theme.Surface.ToArgb() == Color.White.ToArgb());

            // Удалено всё: встроенные драйверы, язык и тема, одно общее уведомление.
            Directory.Delete(AppPaths.Languages, recursive: true);
            Directory.Delete(AppPaths.Drivers, recursive: true);
            Notices.Clear();
            L.Load(null);
            DriverStore.Load();
            ThemeStore.Load();
            ThemeStore.Apply(null);
            Check("Без файлов: драйверы встроенные", DriverStore.All.Count == builtIns.Count);
            Check("Без файлов: русский встроенный", L.Code == L.BuiltInCode && L.T("menu.file", "Файл") == "Файл");
            Check("Без файлов: одно уведомление «языковых пакетов, драйверов и тем»",
                Notices.All.Count == 1 && Notices.All[0].Text == Missing("языковых пакетов, драйверов и тем"), Texts());

            // Языковой пакет переопределяет текст, а ошибка в переводе не роняет программу.
            Directory.CreateDirectory(AppPaths.Languages);
            File.WriteAllText(Path.Combine(AppPaths.Languages, "xx.json"),
                "{\"code\": \"xx\", \"name\": \"Test\", \"strings\": {\"menu.file\": \"File!\", \"broken\": \"oops {3}\"}}");
            L.Load("xx");
            Check("Язык: перевод из файла", L.Code == "xx" && L.T("menu.file", "Файл") == "File!");
            Check("Язык: битая подстановка → встроенный текст", L.F("broken", "ok {0}", 1) == "ok 1");
            L.Load("zz");
            Check("Язык: нет выбранного → уведомление", Notices.All.Any(n => n.Text.Contains("zz")));
            L.Load(null);

            // Пресеты: перенос раскладки из settings.json старой версии, уникальные имена, переименование.
            var legacy = Profile.CreateDefault();
            legacy.Name = "Мой: пресет";
            legacy.Layers[0].Actions[ControlId.Key1] = PadAction.FromMedia(MediaCatalog.Mute);
            var old = new AppSettings { LegacyProfile = legacy };
            SettingsStore.AttachPresets(old);
            Check("Пресеты: старая раскладка перенесена в пресет",
                old.ActivePreset == "Мой_ пресет" && PresetStore.Names().SequenceEqual(new[] { "Мой_ пресет" }) &&
                old.Profile.Layers[0].Get(ControlId.Key1).MediaCode == MediaCatalog.Mute && old.LegacyProfile == null,
                string.Join(", ", PresetStore.Names()));
            Check("Пресеты: свободное имя", PresetStore.Unique("Мой_ пресет") == "Мой_ пресет (2)");
            SettingsStore.Save(old);
            Check("Пресеты: settings.json без раскладки", !File.ReadAllText(SettingsStore.FilePath).Contains("\"Layers\""));
            PresetStore.Rename("Мой_ пресет", "Игры");
            Check("Пресеты: переименование", PresetStore.Names().SequenceEqual(new[] { "Игры" }) &&
                                              PresetStore.Load("Игры").Layers[0].Get(ControlId.Key1).MediaCode == MediaCatalog.Mute);
            var fresh = SettingsStore.LoadSettings();
            fresh.ActivePreset = "удалённый";
            SettingsStore.AttachPresets(fresh);
            Check("Пресеты: пропавший активный пресет → первый из папки", fresh.ActivePreset == "Игры");
        }
        finally
        {
            AppPaths.Root = realRoot;
            Notices.Clear();
            L.Load(null);
            DriverStore.Load();
            ThemeStore.Load();
            ThemeStore.Apply(null);
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    static Dictionary<string, string> ReadStrings(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("strings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    static string Hex(byte[] packet)
    {
        int end = packet.Length;
        while (end > 0 && packet[end - 1] == 0) end--;
        return string.Join(" ", packet.Take(end).Select(b => b.ToString("x2")));
    }
}
