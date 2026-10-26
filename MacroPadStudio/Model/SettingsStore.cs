using System.Text.Json;
using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>
/// Portable-хранение. Общие настройки программы — user_data/settings.json, раскладки — пресеты в
/// user_data/presets (см. <see cref="PresetStore"/>). В профиль пользователя Windows ничего не пишется.
/// </summary>
public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder => AppPaths.UserData;

    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Только settings.json — язык, тема, драйвер и т.п. Пресет подключает <see cref="AttachPresets"/>.</summary>
    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Повреждённый файл не должен мешать запуску: сохраняем копию и начинаем заново.
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
        }
        return new AppSettings();
    }

    /// <summary>
    /// Подключает пресеты: переносит раскладку старой версии из settings.json, создаёт пресет по умолчанию,
    /// если их нет, и загружает активный.
    /// </summary>
    public static void AttachPresets(AppSettings settings)
    {
        var names = PresetStore.Names();
        if (settings.LegacyProfile != null && names.Count == 0)
        {
            string name = PresetStore.Clean(settings.LegacyProfile.Name);
            if (name.Length == 0) name = L.T("preset.default", "Основной");
            settings.LegacyProfile.Normalize();
            PresetStore.Save(name, settings.LegacyProfile);
            settings.ActivePreset = name;
            names = PresetStore.Names();
        }
        settings.LegacyProfile = null;

        if (names.Count == 0)
        {
            string name = PresetStore.Clean(L.T("preset.default", "Основной"));
            PresetStore.Save(name, Profile.CreateDefault());
            names = PresetStore.Names();
        }
        if (settings.ActivePreset == null || !names.Contains(settings.ActivePreset)) settings.ActivePreset = names[0];

        try
        {
            settings.Profile = PresetStore.Load(settings.ActivePreset);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Notices.Add(NoticeLevel.Warning, L.F("notice.presetBad",
                "Пресет «{0}» не удалось прочитать ({1}) — загружен пример настроек.", settings.ActivePreset, ex.Message));
            settings.Profile = Profile.CreateDefault();
        }
        settings.Normalize();
    }

    /// <summary>Настройки вместе с активным пресетом.</summary>
    public static AppSettings Load()
    {
        var settings = LoadSettings();
        AttachPresets(settings);
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
        if (settings.ActivePreset != null) PresetStore.Save(settings.ActivePreset, settings.Profile);
    }

    public static Profile ReadProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException(L.T("preset.invalid", "Файл не содержит пресет."));
        profile.Normalize();
        return profile;
    }

    public static void WriteProfile(Profile profile, string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Json));
        File.Move(temp, path, overwrite: true);
    }
}
