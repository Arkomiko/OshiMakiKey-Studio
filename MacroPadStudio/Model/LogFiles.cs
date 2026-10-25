using System.Text;

namespace OshiMakiKey.Model;

/// <summary>Журналы в user_data. Файл открывается на совместную запись, чтобы монитор и мастер не мешали друг другу.</summary>
public static class LogFiles
{
    public static string MonitorLog => Path.Combine(SettingsStore.Folder, "monitor.log");

    /// <summary>Открывает журнал на дозапись. Null и текст ошибки — если открыть не удалось.</summary>
    public static StreamWriter? OpenAppend(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            error = null;
            return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Сохраняет строки отдельным файлом; возвращает путь или null.</summary>
    public static string? SaveSnapshot(string prefix, IEnumerable<string> lines)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            string path = Path.Combine(SettingsStore.Folder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
