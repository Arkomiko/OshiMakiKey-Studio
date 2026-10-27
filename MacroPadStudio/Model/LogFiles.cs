using System.Text;

namespace OshiMakiKey.Model;

/// <summary>
/// Журналы в user_data. monitor.log хранит не больше <see cref="MonitorLogLines"/> последних событий: монитор и мастер
/// пишут в него через <see cref="CappedLog"/>, который дописывает свои строки к тому, что уже в файле, и обрезает начало.
/// Автоочистка (<see cref="Cleanup"/>) при запуске: старые и лишние diagnostics-*.log удаляются.
/// </summary>
public static class LogFiles
{
    public static string MonitorLog => Path.Combine(AppPaths.UserData, "monitor.log");

    /// <summary>Сколько последних событий хранит monitor.log — чтобы удержание кнопки не раздувало файл.</summary>
    public const int MonitorLogLines = 500;
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

    static void TrimMonitorLog() => CappedLog.Append(MonitorLog, Array.Empty<string>(), MonitorLogLines);

    /// <summary>Открывает monitor.log на запись с ограничением в <see cref="MonitorLogLines"/> строк.</summary>
    public static CappedLog OpenMonitorLog() => new(MonitorLog, MonitorLogLines);

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

/// <summary>
/// Журнал с ограничением по числу строк. Новые строки копятся и раз в 300 мс дописываются в файл: файл читается,
/// к нему добавляются новые строки, лишнее начало отрезается. Так два писателя (монитор и мастер) не затирают друг друга.
/// </summary>
public sealed class CappedLog : IDisposable
{
    readonly string _path;
    readonly int _limit;
    readonly List<string> _pending = new();
    readonly System.Threading.Timer _timer;
    readonly object _lock = new();

    /// <summary>Текст последней ошибки записи (null — всё хорошо).</summary>
    public string? Error { get; private set; }

    public CappedLog(string path, int limit)
    {
        _path = path;
        _limit = limit;
        _timer = new System.Threading.Timer(_ => Flush());
    }

    public void WriteLine(string line)
    {
        lock (_lock) _pending.Add(line);
        _timer.Change(300, Timeout.Infinite);
    }

    public void Flush()
    {
        string[] lines;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            lines = _pending.ToArray();
            _pending.Clear();
        }
        Error = Append(_path, lines, _limit);
    }

    /// <summary>Дописывает строки и оставляет в файле последние <paramref name="limit"/>; возвращает текст ошибки или null.</summary>
    public static string? Append(string path, IReadOnlyCollection<string> lines, int limit)
    {
        try
        {
            if (lines.Count == 0 && !File.Exists(path)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            var all = new List<string>();
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
                while (reader.ReadLine() is string existing) all.Add(existing);
            if (lines.Count == 0 && all.Count <= limit) return null;
            all.AddRange(lines);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            foreach (var line in all.Skip(Math.Max(0, all.Count - limit))) writer.WriteLine(line);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
