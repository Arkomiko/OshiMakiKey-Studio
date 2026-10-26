using System.Text.Json;

namespace OshiMakiKey.Model;

public sealed record LanguageInfo(string Code, string Name, string Path);

/// <summary>
/// Локализация. Каждый текст в коде записан как L.T("ключ", "русский текст"): русский встроен в программу,
/// а файлы language/*.json ({"code", "name", "strings": {ключ: текст}}) переопределяют его. Ключа нет в файле —
/// берётся встроенный текст, поэтому неполный перевод ничего не ломает.
/// </summary>
public static class L
{
    public const string BuiltInCode = "ru";

    static Dictionary<string, string> _strings = new();

    /// <summary>Код текущего языка.</summary>
    public static string Code { get; private set; } = BuiltInCode;

    public static IReadOnlyList<LanguageInfo> Available { get; private set; } = Array.Empty<LanguageInfo>();

    public static string T(string key, string fallback) => _strings.TryGetValue(key, out var text) ? text : fallback;

    public static string F(string key, string fallback, params object?[] args)
    {
        try
        {
            return string.Format(T(key, fallback), args);
        }
        catch (FormatException)
        {
            // Ошибка в переводе (например, лишний {2}) не должна ронять программу.
            return string.Format(fallback, args);
        }
    }

    /// <summary>Название из словаря {"ru": "…", "en": "…"} на текущем языке, иначе английское, иначе любое.</summary>
    public static string Pick(IReadOnlyDictionary<string, string>? names, string fallback) =>
        names == null ? fallback
        : names.TryGetValue(Code, out var name) ? name
        : names.TryGetValue("en", out var english) ? english
        : names.Values.FirstOrDefault() ?? fallback;

    /// <summary>
    /// Находит языковые пакеты и включает нужный. Сообщает в уведомлениях, если файлов нет.
    /// Язык ещё не выбран (null) — берётся язык Windows, если для него есть пакет.
    /// </summary>
    public static void Load(string? preferred)
    {
        _strings = new();
        Code = BuiltInCode;

        var found = new List<LanguageInfo>();
        foreach (var file in AppPaths.JsonFiles(AppPaths.Languages))
        {
            try
            {
                var pack = Read(file);
                if (pack.Code.Length > 0 && found.All(l => l.Code != pack.Code)) found.Add(new LanguageInfo(pack.Code, pack.Name, file));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Notices.AddBadFile(file, ex.Message);
            }
        }
        Available = found;

        if (found.Count == 0)
        {
            Notices.AddMissing(L.T("notice.missing.languages", "языковых пакетов"));
            return;
        }

        string system = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var pick = found.FirstOrDefault(l => l.Code == (preferred ?? system))
                   ?? found.FirstOrDefault(l => l.Code == BuiltInCode)
                   ?? found[0];
        try
        {
            var pack = Read(pick.Path);
            _strings = pack.Strings;
            Code = pick.Code;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Notices.AddBadFile(pick.Path, ex.Message);
            return;
        }

        if (preferred != null && preferred != pick.Code)
            Notices.Add(NoticeLevel.Warning, L.F("notice.languageNotFound", "Языковой пакет «{0}» не найден — используется «{1}».", preferred, pick.Name));
    }

    sealed class Pack
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new();
    }

    static Pack Read(string path)
    {
        var pack = JsonSerializer.Deserialize<Pack>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new JsonException("empty");
        pack.Code = (pack.Code ?? "").Trim().ToLowerInvariant();
        pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? pack.Code : pack.Name.Trim();
        pack.Strings ??= new();
        return pack;
    }
}
