using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Тема оформления (скин): {"id", "name": {"ru", "en"}, "dark", "colors": {"accent": "#2563EB", …}}.
/// Обязательных цветов нет: недостающие выводятся из основных (window, surface, text, accent).
/// Тема — файл .json или .ts (объект в синтаксисе TypeScript), либо пакет: .zip или папка с theme.ts/theme.json
/// и папками img (картинки), animate (кадры анимированного фона) и scripts (только хранятся, не выполняются).
/// </summary>
internal sealed class ThemeDef
{
    public string Id { get; set; } = "";
    public Dictionary<string, string> Name { get; set; } = new();

    /// <summary>Место в списке тем: меньше — раньше.</summary>
    public int Order { get; set; } = 1000;

    public bool Dark { get; set; }
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>Скругление углов: 1 — обычное, 0 — прямые углы (например, у «пиксельных» тем).</summary>
    public float Corners { get; set; } = 1f;

    /// <summary>Встроенная анимированная сцена фона (например, «minecraft»); текстуры она берёт из img пакета.</summary>
    public string? Background { get; set; }

    /// <summary>Картинка фона внутри пакета (например, «img/background.png»).</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>Скорость кадров из папки animate (кадров в секунду).</summary>
    public int AnimationFps { get; set; } = 10;

    /// <summary>Пользовательская тема (папка theme/custom), а не стандартная светлая или тёмная.</summary>
    [JsonIgnore] public bool Custom { get; set; }

    /// <summary>Картинки из img пакета по имени файла без расширения.</summary>
    [JsonIgnore] public Dictionary<string, Image> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Кадры анимированного фона из animate (по порядку имён файлов).</summary>
    [JsonIgnore] public List<Image> Frames { get; } = new();

    [JsonIgnore] public Image? Picture { get; set; }

    [JsonIgnore] public string Title => L.Pick(Name, Id);

    /// <summary>Цвет акцента для кружка в списке тем.</summary>
    [JsonIgnore]
    public Color AccentColor => Theme.Parse(Colors).TryGetValue("accent", out var color) ? color : Color.FromArgb(0x25, 0x63, 0xEB);

    public void DisposeImages()
    {
        foreach (var image in Images.Values) image.Dispose();
        foreach (var frame in Frames) frame.Dispose();
        Images.Clear();
        Frames.Clear();
        Picture = null;
    }
}

internal static class ThemeStore
{
    public const string DefaultId = "light-blue";

    /// <summary>Темы прежних версий → ближайшие новые, чтобы выбор не сбрасывался с уведомлением.</summary>
    static readonly Dictionary<string, string> Renamed = new()
    {
        ["light"] = "light-blue", ["dark"] = "dark-blue", ["graphite"] = "dark-orange", ["midnight"] = "dark-sky",
        ["ocean"] = "light-teal", ["forest"] = "dark-green", ["sakura"] = "light-pink", ["lavender"] = "light-violet",
        ["sunset"] = "dark-coral", ["mint"] = "light-green", ["sand"] = "light-amber", ["contrast"] = "dark-yellow",
    };

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<ThemeDef> All { get; private set; } = Array.Empty<ThemeDef>();

    public static ThemeDef Current { get; private set; } = new();

    // Пакет темы не должен съесть память: ограничения на картинку и на число кадров.
    const long MaxImageBytes = 8 * 1024 * 1024;
    const int MaxFrames = 240;

    /// <summary>
    /// Темы из папки theme: light и dark — стандартные, custom — пользовательские (файлы .json/.ts, пакеты .zip и
    /// папки с theme.ts). Файлы прямо в theme тоже читаются как пользовательские. Тем нет совсем — уведомление.
    /// </summary>
    public static void Load()
    {
        foreach (var old in All) old.DisposeImages();
        var loaded = new List<ThemeDef>();
        var sources = new (string Folder, bool? Dark, bool Custom)[]
        {
            (Path.Combine(AppPaths.Themes, "light"), false, false),
            (Path.Combine(AppPaths.Themes, "dark"), true, false),
            (Path.Combine(AppPaths.Themes, "custom"), null, true),
            (AppPaths.Themes, null, true),
        };
        foreach (var (folder, dark, custom) in sources)
        {
            if (!Directory.Exists(folder)) continue;
            var items = Directory.GetFiles(folder)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".json" or ".ts" or ".zip")
                .Concat(custom && folder != AppPaths.Themes ? Directory.GetDirectories(folder) : Array.Empty<string>())
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                try
                {
                    var theme = Read(item);
                    if (theme == null) continue;
                    if (dark is bool d) theme.Dark = d;
                    theme.Custom = custom;
                    var bad = theme.Colors.Where(c => !Theme.TryParseColor(c.Value, out _)).Select(c => c.Key).ToList();
                    if (bad.Count > 0)
                        Notices.Add(NoticeLevel.Warning, L.F("notice.themeColors",
                            "В теме «{0}» неверные цвета ({1}) — вместо них используются стандартные.", theme.Title, string.Join(", ", bad)));
                    if (loaded.All(t => t.Id != theme.Id)) loaded.Add(theme);
                    else theme.DisposeImages();
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    Notices.AddBadFile(item, ex.Message);
                }
            }
        }

        if (loaded.Count == 0) Notices.AddMissing(L.T("notice.missing.themes", "тем"));
        All = loaded.OrderBy(t => t.Order).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Стандартные темы (светлые и тёмные) — в списке это кружки цвета акцента.</summary>
    public static IReadOnlyList<ThemeDef> Standard => (All.Count > 0 ? All : new[] { Current }).Where(t => !t.Custom).ToList();

    /// <summary>Пользовательские темы — в списке это карточки с миниатюрой.</summary>
    public static IReadOnlyList<ThemeDef> CustomThemes => All.Where(t => t.Custom).ToList();

    /// <summary>Файлы пакета темы: путь внутри пакета (через «/») → открытие для чтения.</summary>
    sealed record Entry(string Path, long Length, Func<Stream> Open);

    /// <summary>Читает тему из файла .json/.ts, пакета .zip или папки-пакета; папка без theme.* — не тема (null).</summary>
    static ThemeDef? Read(string path)
    {
        if (Directory.Exists(path))
        {
            string root = Path.GetFullPath(path);
            var entries = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => new Entry(Path.GetRelativePath(root, f).Replace('\\', '/'), new FileInfo(f).Length, () => File.OpenRead(f)))
                .ToList();
            return FromPackage(entries, Path.GetFileName(root), mustExist: false);
        }
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Where(e => e.Name.Length > 0).Select(e => new Entry(e.FullName.Replace('\\', '/'), e.Length, e.Open)).ToList();
            return FromPackage(entries, Path.GetFileNameWithoutExtension(path), mustExist: true);
        }
        return Parse(path, File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    static ThemeDef? FromPackage(List<Entry> entries, string fallbackId, bool mustExist)
    {
        // Описание темы — theme.ts/theme.json в корне (или первый .ts/.json, если он один в корне).
        var main = entries.Where(e => !e.Path.Contains('/') && (e.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || e.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => Path.GetFileNameWithoutExtension(e.Path).Equals("theme", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (main == null)
            return mustExist ? throw new InvalidDataException(L.T("notice.themePackageEmpty", "в пакете темы нет файла theme.ts или theme.json")) : null;
        string text;
        using (var reader = new StreamReader(main.Open())) text = reader.ReadToEnd();
        var theme = Parse(main.Path, text, fallbackId);

        static bool In(Entry e, string folder) => e.Path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        static bool IsImage(Entry e) => Path.GetExtension(e.Path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp";
        foreach (var entry in entries.Where(e => In(e, "img") && IsImage(e) && e.Length <= MaxImageBytes))
            theme.Images[Path.GetFileNameWithoutExtension(entry.Path)] = LoadImage(entry);
        foreach (var entry in entries.Where(e => In(e, "animate") && IsImage(e) && e.Length <= MaxImageBytes)
                     .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Take(MaxFrames))
            theme.Frames.Add(LoadImage(entry));
        // Папка scripts только хранится в пакете: программа никогда не выполняет код из тем.

        var picture = theme.BackgroundImage?.Replace('\\', '/').TrimStart('/');
        theme.Picture = picture != null && theme.Images.TryGetValue(Path.GetFileNameWithoutExtension(picture), out var named) ? named
            : theme.Images.TryGetValue("background", out var background) ? background : null;
        return theme;
    }

    static Image LoadImage(Entry entry)
    {
        using var stream = new MemoryStream();
        using (var source = entry.Open()) source.CopyTo(stream);
        stream.Position = 0;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    static ThemeDef Parse(string name, string text, string fallbackId)
    {
        if (name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) text = TsToJson(text);
        var theme = JsonSerializer.Deserialize<ThemeDef>(text, Json) ?? throw new JsonException("empty");
        theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? fallbackId : theme.Id.Trim();
        theme.Name ??= new();
        theme.Colors ??= new();
        return theme;
    }

    /// <summary>
    /// Тема в синтаксисе TypeScript (export default { id: 'x', colors: { accent: '#…' } } satisfies Theme;) → JSON:
    /// без комментариев и обёртки, ключи в кавычках, строки в двойных кавычках. Код из файла не выполняется.
    /// </summary>
    internal static string TsToJson(string ts)
    {
        string s = Regex.Replace(ts, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"(^|[\s,{\[])//[^\n]*", "$1");
        int start = s.IndexOf('{'), end = s.LastIndexOf('}');
        if (start < 0 || end < start) throw new JsonException(L.T("notice.themeTsObject", "в файле нет объекта темы { … }"));
        s = s[start..(end + 1)];
        s = Regex.Replace(s, @"'((?:[^'\\]|\\.)*)'", m => JsonSerializer.Serialize(m.Groups[1].Value.Replace("\\'", "'").Replace("\\\\", "\\")));
        s = Regex.Replace(s, @"([{,]\s*)([A-Za-z_$][\w$]*)\s*:", "$1\"$2\":");
        return s;
    }

    /// <summary>Включает тему по id; нет такой — тему по умолчанию (и уведомление), нет файлов — встроенную.</summary>
    public static void Apply(string? id)
    {
        if (id != null && Renamed.TryGetValue(id, out var renamed) && All.All(t => t.Id != id)) id = renamed;
        var pick = All.FirstOrDefault(t => t.Id == id)
                   ?? All.FirstOrDefault(t => t.Id == DefaultId)
                   ?? All.FirstOrDefault()
                   ?? BuiltIn();
        if (id != null && All.Count > 0 && pick.Id != id)
            Notices.Add(NoticeLevel.Warning, L.F("notice.themeNotFound", "Тема «{0}» не найдена — используется «{1}».", id, pick.Title));

        Current = pick;
        Theme.Apply(pick);
        ToolStripManager.Renderer = Theme.CreateRenderer();
    }

    /// <summary>Тема, зашитая в программу: светлая палитра без файла.</summary>
    public static ThemeDef BuiltIn() => new()
    {
        Id = "",
        Name = new() { [L.Code] = L.T("theme.builtin", "Встроенная") },
    };

    /// <summary>Миниатюра окна в цветах темы: шапка, плата с клавишами, карточка с кнопкой.</summary>
    public static Bitmap Preview(ThemeDef theme, int width, int height)
    {
        Theme.Apply(theme);
        var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = width / 160f;
            var outer = new RectangleF(0.5f, 0.5f, width - 1.5f, height - 1.5f);
            using (var path = Theme.Rounded(outer, 6 * u))
            using (var brush = new SolidBrush(Theme.Window))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(brush, path);
                // Свой фон темы (сцена, кадр или картинка) — внутри рамки миниатюры.
                g.SetClip(path);
                ThemeBackdrop.DrawPreview(g, theme, Rectangle.Round(outer));
                g.ResetClip();
                g.DrawPath(pen, path);
            }
            using (var brush = new SolidBrush(Theme.Surface)) g.FillRectangle(brush, 1, 1, width - 2, 14 * u);
            using (var brush = new SolidBrush(Theme.Accent))
            using (var path = Theme.Rounded(new RectangleF(width - 44 * u, 4 * u, 38 * u, 7 * u), 2 * u))
                g.FillPath(brush, path);
            using (var brush = new SolidBrush(Theme.Success)) g.FillEllipse(brush, 6 * u, 5.5f * u, 4 * u, 4 * u);
            using (var brush = new SolidBrush(Theme.Text)) g.FillRectangle(brush, 13 * u, 6.5f * u, 30 * u, 2 * u);

            var left = new RectangleF(6 * u, 20 * u, 64 * u, height - 26 * u);
            var right = new RectangleF(76 * u, 20 * u, width - 82 * u, height - 26 * u);
            foreach (var card in new[] { left, right })
                using (var path = Theme.Rounded(card, 4 * u))
                using (var brush = new SolidBrush(Theme.Surface))
                    g.FillPath(brush, path);

            var pad = new RectangleF(left.X + 5 * u, left.Y + 6 * u, left.Width - 10 * u, 28 * u);
            using (var path = Theme.Rounded(pad, 4 * u))
            using (var brush = new LinearGradientBrush(pad, Theme.PadTop, Theme.PadBottom, 90f))
                g.FillPath(brush, path);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    var key = new RectangleF(pad.X + 4 * u + c * 10 * u, pad.Y + 4 * u + r * 10 * u, 8 * u, 8 * u);
                    using var path = Theme.Rounded(key, 1.5f * u);
                    using var brush = new SolidBrush(r == 0 && c == 0 ? Theme.KeySelectedBottom : Theme.KeyTop);
                    g.FillPath(brush, path);
                }
            using (var brush = new SolidBrush(Theme.KnobTop)) g.FillEllipse(brush, pad.Right - 17 * u, pad.Y + 5 * u, 18 * u * 0.85f, 18 * u * 0.85f);
            for (int i = 0; i < 3; i++)
                using (var brush = new SolidBrush(i == 0 ? Theme.AccentSoft : Theme.Hover))
                    g.FillRectangle(brush, left.X + 5 * u, pad.Bottom + 5 * u + i * 8 * u, left.Width - 10 * u, 6 * u);

            using (var brush = new SolidBrush(Theme.Text)) g.FillRectangle(brush, right.X + 6 * u, right.Y + 6 * u, 34 * u, 4 * u);
            using (var brush = new SolidBrush(Theme.Muted)) g.FillRectangle(brush, right.X + 6 * u, right.Y + 13 * u, right.Width - 20 * u, 2 * u);
            for (int i = 0; i < 3; i++)
                using (var path = Theme.Rounded(new RectangleF(right.X + 6 * u + i * 22 * u, right.Y + 20 * u, 19 * u, 8 * u), 2 * u))
                using (var brush = new SolidBrush(i == 1 ? Theme.Accent : Theme.Window))
                    g.FillPath(brush, path);
            using (var brush = new SolidBrush(Theme.SuccessSoft)) g.FillRectangle(brush, right.X + 6 * u, right.Y + 33 * u, right.Width - 12 * u, 8 * u);
        }
        Theme.Apply(Current);
        return bitmap;
    }

}
