using System.Text.Json;
using System.Text.Json.Serialization;

namespace OshiMakiKey.Model;

/// <summary>
/// Portable-хранение: настройки и журнал лежат в папке user_data рядом с программой, ничего не пишется
/// в профиль пользователя Windows. Плюс экспорт/импорт профилей.
/// </summary>
public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "user_data");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (settings != null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Повреждённый файл не должен мешать запуску: сохраняем копию и начинаем с примера.
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
        }

        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static Profile LoadProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Файл не содержит профиль.");
        profile.Normalize();
        return profile;
    }

    public static void SaveProfile(Profile profile, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Json));
}
