using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace OshiMakiKey.Setup;

/// <summary>
/// Установка и удаление для текущего пользователя (без прав администратора): программа — в
/// %LocalAppData%\Programs\OshiMakiKey Studio, ярлыки — в меню «Пуск» и на рабочем столе, запись — в «Установленные приложения».
/// Папка user_data (настройки и пресеты) и свои темы из themes\custom при обновлении не трогаются.
/// </summary>
internal static class Installer
{
    public const string AppName = "OshiMakiKey Studio";
    public const string ExeName = "OshiMakiKeyStudio.exe";
    public const string Version = "0.1.6b";
    public const string Publisher = "Arkomiko";
    public const string Website = "https://github.com/Arkomiko/OshiMakiKey-Studio";
#if PAYLOAD_X64
    public const string Arch = "x64";
#else
    public const string Arch = "x86";
#endif

    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OshiMakiKeyStudio";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ServiceName = "OMK_Studio-Service";
    const string UninstallerName = "uninstall.exe";

    /// <summary>Интерфейс на русском для ru/be/uk/kk, иначе по-английски.</summary>
    public static bool Russian { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "be" or "uk" or "kk";

    public static string T(string ru, string en) => Russian ? ru : en;

    /// <summary>Папка прошлой установки (из «Установленных приложений») или папка по умолчанию.</summary>
    public static string DefaultFolder
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is string previous && previous.Length > 0) return previous;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }
    }

    static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>Устанавливает или обновляет программу; progress — доля 0..1.</summary>
    public static void Install(string folder, bool desktopShortcut, Action<double>? progress = null)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        StopRunning(folder);

        // Старые стандартные ресурсы убираем, чтобы не оставались удалённые в новой версии файлы; user_data и свои темы — остаются.
        foreach (var old in new[] { "drivers", "language", Path.Combine("themes", "light"), Path.Combine("themes", "dark") })
        {
            string path = Path.Combine(folder, old);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }

        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new InvalidOperationException("payload.zip"))
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                string target = Path.GetFullPath(Path.Combine(folder, files[i].FullName.Replace('/', Path.DirectorySeparatorChar)));
                // Защита от путей вида ..\ в архиве: всё строго внутри папки установки.
                if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                files[i].ExtractToFile(target, overwrite: true);
                progress?.Invoke((i + 1) / (double)files.Count * 0.9);
            }
        }

        string exe = Path.Combine(folder, ExeName);
        string uninstaller = Path.Combine(folder, UninstallerName);
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(self), uninstaller, StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninstaller, overwrite: true);

        CreateShortcut(StartMenuShortcut, exe, folder);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, exe, folder);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", Publisher);
            key.SetValue("URLInfoAbout", Website);
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", folder);
            key.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /uninstall /S");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(FolderSize(folder) / 1024), RegistryValueKind.DWord);
        }
        progress?.Invoke(1);
    }

    /// <summary>Удаляет программу. keepData — оставить user_data (настройки и пресеты).</summary>
    public static void Uninstall(string folder, bool keepData)
    {
        folder = Path.GetFullPath(folder);
        StopRunning(folder);
        string exe = Path.Combine(folder, ExeName);

        // Служба Windows ставилась с правами администратора — и удаляется так же (запрос UAC).
        using (var service = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}"))
            if (service != null && File.Exists(exe))
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(exe, "--remove-winservice") { UseShellExecute = true, Verb = "runas" });
                    process?.WaitForExit(30000);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Отказались в окне UAC — служба останется, её можно удалить позже из настроек другой копии программы.
                }
            }
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue("OshiMakiKeyStudio", throwOnMissingValue: false);
            run?.DeleteValue(ServiceName, throwOnMissingValue: false);
        }

        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut })
            if (File.Exists(shortcut)) File.Delete(shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder)) TryDelete(file);
        foreach (var dir in Directory.GetDirectories(folder))
        {
            if (keepData && Path.GetFileName(dir).Equals("user_data", StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!Directory.EnumerateFileSystemEntries(folder).Any())
        {
            try { Directory.Delete(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Закрывает копии программы (окно и помощник службы), запущенные из этой папки.</summary>
    static void StopRunning(string folder)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (path != null && path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    static void CreateShortcut(string path, string target, string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = folder;
        link.IconLocation = target + ",0";
        link.Description = AppName;
        link.Save();
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static long FolderSize(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "user_data" + Path.DirectorySeparatorChar))
            .Sum(f => new FileInfo(f).Length);
}
