using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.Json.Serialization;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Тема оформления — файл theme/*.json: {"id", "name": {"ru", "en"}, "dark", "colors": {"accent": "#2563EB", …}}.
/// Обязательных цветов нет: недостающие выводятся из основных (window, surface, text, accent).
/// </summary>
internal sealed class ThemeDef
{
    public string Id { get; set; } = "";
    public Dictionary<string, string> Name { get; set; } = new();

    /// <summary>Место в меню «Тема»: меньше — выше.</summary>
    public int Order { get; set; } = 1000;

    public bool Dark { get; set; }
    public Dictionary<string, string> Colors { get; set; } = new();

    [JsonIgnore]
    public string Title => L.Pick(Name, Id);
}

internal static class ThemeStore
{
    public const string DefaultId = "light";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<ThemeDef> All { get; private set; } = Array.Empty<ThemeDef>();

    public static ThemeDef Current { get; private set; } = new();

    /// <summary>Темы из папки theme; если их нет — уведомление (останется встроенная).</summary>
    public static void Load()
    {
        var loaded = new List<ThemeDef>();
        foreach (var file in AppPaths.JsonFiles(AppPaths.Themes))
        {
            try
            {
                var theme = JsonSerializer.Deserialize<ThemeDef>(File.ReadAllText(file), Json) ?? throw new JsonException("empty");
                theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? Path.GetFileNameWithoutExtension(file) : theme.Id.Trim();
                theme.Name ??= new();
                theme.Colors ??= new();
                var bad = theme.Colors.Where(c => !Theme.TryParseColor(c.Value, out _)).Select(c => c.Key).ToList();
                if (bad.Count > 0)
                    Notices.Add(NoticeLevel.Warning, L.F("notice.themeColors",
                        "В теме «{0}» неверные цвета ({1}) — вместо них используются стандартные.", theme.Title, string.Join(", ", bad)));
                if (loaded.All(t => t.Id != theme.Id)) loaded.Add(theme);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Notices.AddBadFile(file, ex.Message);
            }
        }

        if (loaded.Count == 0) Notices.AddMissing(L.T("notice.missing.themes", "тем"));
        All = loaded.OrderBy(t => t.Order).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Включает тему по id; нет такой — тему по умолчанию (и уведомление), нет файлов — встроенную.</summary>
    public static void Apply(string? id)
    {
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

    /// <summary>Значок темы для меню: фон окна, карточка и акцентная точка.</summary>
    public static Bitmap Swatch(ThemeDef theme, int size)
    {
        Theme.Apply(theme);
        Color window = Theme.Window, surface = Theme.Surface, border = Theme.Border, accent = Theme.Accent;
        Theme.Apply(Current);

        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(0.5f, 0.5f, size - 1.5f, size - 1.5f);
        using (var path = Theme.Rounded(outer, size / 5f))
        using (var brush = new SolidBrush(window))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        using (var card = Theme.Rounded(new RectangleF(size * 0.18f, size * 0.18f, size * 0.64f, size * 0.64f), size / 8f))
        using (var brush = new SolidBrush(surface))
            g.FillPath(brush, card);
        float dot = size * 0.36f;
        using (var brush = new SolidBrush(accent))
            g.FillEllipse(brush, (size - dot) / 2, (size - dot) / 2, dot, dot);
        return bitmap;
    }
}
