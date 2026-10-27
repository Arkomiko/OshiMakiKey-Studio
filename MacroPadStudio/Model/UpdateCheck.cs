using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace OshiMakiKey.Model;

/// <summary>
/// «Проверить обновления и файлы»: последняя версия из релизов GitHub и целостность ресурсов рядом с exe
/// (драйверы, темы, языки читаются как данные). Ничего не скачивает — страницу релиза открывает пользователь сам.
/// </summary>
internal static class UpdateCheck
{
    const string LatestRelease = "https://api.github.com/repos/Arkomiko/OshiMakiKey-Studio/releases/latest";

    /// <summary>Итог проверки версии: Error — текст ошибки сети; Latest == null и Error == null — релизов ещё нет.</summary>
    public sealed record Release(string? Latest, string? Url, string? Notes, bool Newer, string? Error);

    public static async Task<Release> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Branding.InternalName}/{Branding.Version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync(LatestRelease);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new Release(null, null, null, false, null);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string? url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            string? notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new Release(tag, url, notes, Compare(tag, Branding.Version) > 0, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new Release(null, null, null, false, ex.Message);
        }
    }

    /// <summary>Сравнение версий вида «v0.1.6b»: сначала числа, при равенстве — буквенный хвост (b &lt; пусто, то есть бета раньше релиза).</summary>
    public static int Compare(string a, string b)
    {
        static (Version Number, string Suffix) Parse(string text)
        {
            text = text.Trim().TrimStart('v', 'V');
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
            return (Version.TryParse(text[..end].Trim('.'), out var number) ? number : new Version(0, 0), text[end..]);
        }
        var (na, sa) = Parse(a);
        var (nb, sb) = Parse(b);
        int byNumber = na.CompareTo(nb);
        if (byNumber != 0) return byNumber;
        if (sa == sb) return 0;
        if (sa.Length == 0) return 1;
        if (sb.Length == 0) return -1;
        return string.CompareOrdinal(sa, sb);
    }

    /// <summary>Строки отчёта о файлах программы: сколько файлов в каждой папке ресурсов и какие не читаются.</summary>
    public static List<string> CheckFiles(out bool ok)
    {
        var report = new List<string>();
        ok = true;
        foreach (var (title, folder, recursive) in new[]
                 {
                     (L.T("updates.files.drivers", "Драйверы"), AppPaths.Drivers, false),
                     (L.T("updates.files.themes", "Темы"), AppPaths.Themes, true),
                     (L.T("updates.files.languages", "Языки"), AppPaths.Languages, false),
                 })
        {
            if (!Directory.Exists(folder))
            {
                report.Add(L.F("updates.files.missing", "✗ {0}: папки {1} нет", title, Path.GetFileName(folder)));
                ok = false;
                continue;
            }
            var files = Directory.GetFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".json" or ".zip").ToList();
            var broken = files.Where(f => !Readable(f)).Select(Path.GetFileName).ToList();
            if (files.Count == 0 || broken.Count > 0) ok = false;
            report.Add(broken.Count == 0 && files.Count > 0
                ? L.F("updates.files.ok", "✓ {0}: файлов {1}, все читаются", title, files.Count)
                : files.Count == 0 ? L.F("updates.files.empty", "✗ {0}: в папке нет файлов", title)
                : L.F("updates.files.broken", "✗ {0}: не читаются {1}", title, string.Join(", ", broken)));
        }
        return report;
    }

    static bool Readable(string file)
    {
        try
        {
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(file);
                return zip.Entries.Count > 0;
            }
            using var json = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }
}
