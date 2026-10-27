using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace OshiMakiKey.Setup;

/// <summary>
/// Параметры: без параметров — окно установки; /S — тихая установка, /D=папка — куда, /NODESKTOP — без ярлыка на рабочем столе;
/// /uninstall — удаление (с /S — без вопросов, /KEEPDATA — оставить user_data).
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Has(args, "/S");
        string? folderArg = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))?.Substring(3).Trim('"');
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (Has(args, "/uninstall")) return Uninstall(args, silent, folderArg);

#if PAYLOAD_X64
        if (!Environment.Is64BitOperatingSystem)
        {
            Fail(silent, Installer.T("Эта версия — для 64-битной Windows. Скачайте установщик x86.", "This build is for 64-bit Windows. Download the x86 installer."));
            return 2;
        }
#endif
        if (silent)
        {
            try
            {
                Installer.Install(folderArg ?? Installer.DefaultFolder, desktopShortcut: !Has(args, "/NODESKTOP"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
        Application.Run(new SetupForm(folderArg ?? Installer.DefaultFolder));
        return 0;
    }

    static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    static int Uninstall(string[] args, bool silent, string? folderArg)
    {
        string self = Assembly.GetExecutingAssembly().Location;
        string folder = folderArg ?? Path.GetDirectoryName(self)!;

        // Из папки программы себя не удалить — копируемся во временную папку и продолжаем оттуда.
        if (folderArg == null)
        {
            string temp = Path.Combine(Path.GetTempPath(), "OshiMakiKeyUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            File.Copy(self, temp);
            string rest = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Process.Start(new ProcessStartInfo(temp, $"{rest} /D=\"{folder}\"") { UseShellExecute = false })?.Dispose();
            return 0;
        }

        bool keepData = Has(args, "/KEEPDATA");
        if (!silent)
        {
            var answer = MessageBox.Show(
                Installer.T($"Удалить {Installer.AppName}?\n\nОставить ваши настройки и пресеты (папку user_data)? «Да» — оставить, «Нет» — удалить всё.",
                    $"Uninstall {Installer.AppName}?\n\nKeep your settings and presets (the user_data folder)? Yes — keep, No — delete everything."),
                Installer.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return 0;
            keepData = answer == DialogResult.Yes;
        }
        try
        {
            Installer.Uninstall(folder, keepData);
            if (!silent)
                MessageBox.Show(Installer.T($"{Installer.AppName} удалена.", $"{Installer.AppName} has been removed."), Installer.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Fail(silent, ex.Message);
        }
        // Временная копия удаляет себя после выхода.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\"") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
        return 0;
    }

    static void Fail(bool silent, string message)
    {
        if (silent) Console.Error.WriteLine(message);
        else MessageBox.Show(message, Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
