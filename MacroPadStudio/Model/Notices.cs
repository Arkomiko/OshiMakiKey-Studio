namespace OshiMakiKey.Model;

public enum NoticeLevel { Info, Warning, Error }

/// <summary>SuggestUpdate — рядом с уведомлением показать кнопку «Проверить обновления».</summary>
public sealed record Notice(NoticeLevel Level, string Text, bool SuggestUpdate = false);

/// <summary>Внутренние уведомления программы: отсутствующие или повреждённые файлы ресурсов и т.п.</summary>
public static class Notices
{
    static readonly List<Notice> Items = new();
    static readonly List<string> Missing = new();
    static Notice? _missingNotice;

    public static IReadOnlyList<Notice> All => Items;

    public static event Action? Changed;

    public static void Add(NoticeLevel level, string text, bool suggestUpdate = false)
    {
        if (Items.Any(n => n.Text == text)) return;
        Items.Add(new Notice(level, text, suggestUpdate));
        Changed?.Invoke();
    }

    public static void Remove(Notice notice)
    {
        if (Items.Remove(notice)) Changed?.Invoke();
    }

    public static void Clear()
    {
        Items.Clear();
        Missing.Clear();
        _missingNotice = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Уведомление «нет файлов …»: используются встроенные настройки. Несколько видов файлов сводятся в одно
    /// уведомление — «Отсутствуют файлы тем, языковых пакетов и драйверов, …».
    /// </summary>
    public static void AddMissing(string what)
    {
        if (Missing.Contains(what)) return;
        Missing.Add(what);
        string list = Missing.Count == 1
            ? Missing[0]
            : string.Join(", ", Missing.Take(Missing.Count - 1)) + L.T("notice.and", " и ") + Missing[^1];
        string text = L.F("notice.missing",
            "Отсутствуют файлы {0}, в данный момент применяются стандартные встроенные настройки программы. Рекомендуется проверить программу на наличие обновлений.",
            list);
        var notice = new Notice(NoticeLevel.Warning, char.ToUpper(text[0]) + text[1..], SuggestUpdate: true);

        int index = _missingNotice == null ? -1 : Items.IndexOf(_missingNotice);
        if (index >= 0) Items[index] = notice;
        else Items.Add(notice);
        _missingNotice = notice;
        Changed?.Invoke();
    }

    public static void AddBadFile(string path, string error) =>
        Add(NoticeLevel.Warning, L.F("notice.badFile", "Файл «{0}» повреждён и пропущен: {1}", Path.GetFileName(path), error));
}
