using OshiMakiKey.Model;
using OshiMakiKey.UI;

namespace OshiMakiKey;

internal static class Program
{
    const string MutexName = Branding.InternalName + ".SingleInstance";
    const string ShowEventName = Branding.InternalName + ".ShowWindow";

    /// <summary>
    /// Аргументы:
    ///   --tray               запуск свёрнутым в трей (используется автозапуском)
    ///   --selftest файл      проверка протокола и модели, отчёт в файл
    ///   --probe файл         поиск устройства и тестовое открытие, отчёт в файл
    ///   --snapshot файл.png  снимок окна (для проверки интерфейса), с --select Key1|KnobLeft|… и --kind Keys|Media|Mouse|System
    ///   --monitortest файл   открыть монитор, программно нажать F24 и сохранить журнал (проверка монитора)
    ///   --wizard-snapshot файл.png  снимок окна мастера подбора
    ///   --export-icon файл   сохранить иконку приложения в .ico
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        string? Arg(string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        if (Arg("--selftest") is string testReport) return SelfTest.Run(testReport);
        if (Arg("--probe") is string probeReport) return SelfTest.Probe(probeReport);

        ApplicationConfiguration.Initialize();
        Theme.Init();

        if (Arg("--monitortest") is string monitorReport)
        {
            Application.Run(new MonitorForm(monitorReport));
            return 0;
        }

        if (Arg("--wizard-snapshot") is string wizardShot)
        {
            var wizard = new ProtocolWizardForm();
            wizard.Shown += async (_, _) =>
            {
                await Task.Delay(400);
                using var bitmap = new Bitmap(wizard.Width, wizard.Height);
                wizard.DrawToBitmap(bitmap, new Rectangle(Point.Empty, wizard.Size));
                bitmap.Save(wizardShot, System.Drawing.Imaging.ImageFormat.Png);
                wizard.Close();
            };
            Application.Run(wizard);
            return 0;
        }

        if (Arg("--export-icon") is string iconPath)
        {
            File.WriteAllBytes(iconPath, Theme.BuildIconBytes());
            return 0;
        }

        if (Arg("--snapshot") is string snapshotPath)
        {
            ControlId? select = Enum.TryParse<ControlId>(Arg("--select"), out var id) ? id : null;
            var snapshotSettings = SettingsStore.Load();
            // Только в памяти: в режиме снимка настройки не сохраняются.
            if (select is ControlId selected && Enum.TryParse<ActionKind>(Arg("--kind"), out var kind))
            {
                snapshotSettings.Profile.Layers[0].Get(selected).Kind = kind;
                snapshotSettings.EnsureTriggers();
            }
            Application.Run(new MainForm(snapshotSettings, false, null, snapshotPath, select));
            return 0;
        }

        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        Application.Run(new MainForm(SettingsStore.Load(), args.Contains("--tray"), showEvent));
        return 0;
    }
}
