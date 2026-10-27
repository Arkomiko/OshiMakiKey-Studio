namespace OshiMakiKey.Model;

/// <summary>
/// Папки portable-программы. Ресурсы (драйверы, темы, языки) лежат рядом с exe и обновляются вместе с ней;
/// данные пользователя — в user_data, их программа не перезаписывает.
/// </summary>
public static class AppPaths
{
    /// <summary>Папка программы. Меняется только в самопроверке, чтобы проверить работу без файлов ресурсов.</summary>
    public static string Root { get; set; } = AppContext.BaseDirectory;

    public static string UserData => Path.Combine(Root, "user_data");
    public static string Presets => Path.Combine(UserData, "presets");

    public static string Drivers => Path.Combine(Root, "drivers");
    public static string Themes => Path.Combine(Root, "themes");

    /// <summary>Папка тем прежних версий (theme) — читается, если осталась после обновления поверх.</summary>
    public static string LegacyThemes => Path.Combine(Root, "theme");
    public static string Languages => Path.Combine(Root, "language");

    /// <summary>Файлы *.json в папке ресурсов (пусто, если папки нет).</summary>
    public static string[] JsonFiles(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
}
