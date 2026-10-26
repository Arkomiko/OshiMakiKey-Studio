using System.Text;

namespace OshiMakiKey.Model;

/// <summary>
/// Журналы в user_data. Файл открывается на совместную запись, чтобы монитор и мастер не мешали друг другу.
/// Автоочистка (<see cref="Cleanup"/>) при запуске: monitor.log укорачивается до последних записей,
/// старые и лишние diagnostics-*.log удаляются.
/// </summary>
public static class LogFiles
{
    public static string MonitorLog => Path.Combine(AppPaths.UserData, "monitor.log");

    const long MonitorLogLimit = 512 * 1024;   // больше — укорачиваем…
    const int MonitorLogKeep = 128 * 1024;     // …до последних 128 КБ
    const int DiagnosticsKeepDays = 14;
    const int DiagnosticsKeepCount = 10;

    /// <summary>Укорачивает monitor.log и удаляет старые протоколы проверки записи. Ошибки молча пропускаются.</summary>
    public static void Cleanup()
    {
        try
        {
            TrimMonitorLog();
            var folder = new DirectoryInfo(AppPaths.UserData);
            if (!folder.Exists) return;
            var diagnostics = folder.GetFiles("diagnostics-*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            for (int i = 0; i < diagnostics.Count; i++)
                if (i >= DiagnosticsKeepCount || diagnostics[i].LastWriteTimeUtc < DateTime.UtcNow.AddDays(-DiagnosticsKeepDays))
                    diagnostics[i].Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static void TrimMonitorLog()
    {
        var file = new FileInfo(MonitorLog);
        if (!file.Exists || file.Length <= MonitorLogLimit) return;
        byte[] tail = new byte[MonitorLogKeep];
        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(-MonitorLogKeep, SeekOrigin.End);
            stream.ReadExactly(tail);
        }
        // Начинаем с целой строки, чтобы не оставить обрывок в начале файла.
        int start = Array.IndexOf(tail, (byte)'\n') + 1;
        using var output = new FileStream(file.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        output.Write(tail, start, tail.Length - start);
    }

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
            Directory.CreateDirectory(AppPaths.UserData);
            string path = Path.Combine(AppPaths.UserData, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
