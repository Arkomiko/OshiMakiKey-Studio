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
