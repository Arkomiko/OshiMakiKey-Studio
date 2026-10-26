namespace OshiMakiKey.Model;

/// <summary>Пресеты — раскладки клавиш и ручки, по файлу на каждый: user_data/presets/&lt;название&gt;.json.</summary>
public static class PresetStore
{
    public static string Folder => AppPaths.Presets;

    public static List<string> Names() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : new List<string>();

    public static bool Exists(string name) => File.Exists(PathOf(name));

    public static Profile Load(string name) => SettingsStore.ReadProfile(PathOf(name));

    public static void Save(string name, Profile profile)
    {
        Directory.CreateDirectory(Folder);
        profile.Name = name;
        SettingsStore.WriteProfile(profile, PathOf(name));
    }

    public static void Delete(string name) => File.Delete(PathOf(name));

    public static void Rename(string from, string to) => File.Move(PathOf(from), PathOf(to));

    /// <summary>Название, пригодное как имя файла: без запрещённых символов, не длиннее 60 знаков.</summary>
    public static string Clean(string? name)
    {
        string cleaned = string.Concat((name ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim('.', ' ');
        return cleaned.Length > 60 ? cleaned[..60].Trim() : cleaned;
    }

    /// <summary>Свободное название: «Имя», «Имя (2)», «Имя (3)»…</summary>
    public static string Unique(string baseName)
    {
        string name = Clean(baseName);
        if (!Exists(name)) return name;
        for (int i = 2; ; i++)
            if (!Exists($"{name} ({i})")) return $"{name} ({i})";
    }

    static string PathOf(string name) => Path.Combine(Folder, Clean(name) + ".json");
}
