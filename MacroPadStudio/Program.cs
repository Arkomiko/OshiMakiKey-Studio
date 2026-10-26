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
    ///   --selftest файл      проверка протокола, модели и файлов ресурсов, отчёт в файл
    ///   --probe файл         поиск устройства и тестовое открытие, отчёт в файл
    ///   --snapshot файл.png  снимок окна (для проверки интерфейса), с --select Key1|KnobLeft|… и --kind Keys|Media|Mouse|System,
    ///                        --theme id и --lang код — только для этого снимка
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

        if (Arg("--export-icon") is string iconPath)
        {
            File.WriteAllBytes(iconPath, Theme.BuildIconBytes());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Theme.Init();

        // Фоновая служба OMK_Studio-Service — тот же exe без окна.
        if (args.Contains("--service")) return Agent.OmkService.Run();

        var settings = SettingsStore.LoadSettings();

        if (Arg("--monitortest") is string monitorReport)
        {
            LoadResources(settings);
            Application.Run(new MonitorForm(monitorReport));
            return 0;
        }

        if (Arg("--wizard-snapshot") is string wizardShot)
        {
            LoadResources(settings);
            SettingsStore.AttachPresets(settings);
            var wizard = new ProtocolWizardForm(settings.EffectiveDriver);
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

        if (Arg("--form-snapshot") is string formName && args.Length > Array.IndexOf(args, "--form-snapshot") + 2)
        {
            string path = args[Array.IndexOf(args, "--form-snapshot") + 2];
            settings.Theme = Arg("--theme") ?? settings.Theme;
            settings.Language = Arg("--lang") ?? settings.Language;
            settings.DriverId = Arg("--driver") ?? settings.DriverId;
            LoadResources(settings);
            SettingsStore.AttachPresets(settings);
            Form form = formName switch
            {
                "about" => new AboutForm(settings),
                "setup-0" or "setup-1" or "setup-2" => new SetupWizardForm(settings, formName[^1] - '0'),
                "overwrite" => new FormShot(() => ThemedDialog.Show(null, Branding.AppName, L.T("drop.overwrite.heading", "Внимание, вы хотите перезаписать комбинацию на клавише!"),
                    L.F("drop.overwrite.text", "Сейчас на «{0}»: {1}\nБудет: {2}", "Клавиша 4", "Ctrl + V", "Предыдущий трек"),
                    new[] { L.T("drop.overwrite", "Перезаписать"), L.T("button.cancel", "Отмена") }, DialogKind.Warning, L.T("drop.dontAsk", "Больше не показывать это сообщение"), 1)),
                _ when formName.StartsWith("settings") => new SettingsForm(settings, null, formName.Length > 8 ? formName[8] - '0' : 0),
                _ => throw new ArgumentException(formName),
            };
            if (form is FormShot shot)
            {
                shot.SnapshotPath = path;
                Application.Run(shot);
            }
            else SnapshotForm(form, path);
            return 0;
        }

        if (Arg("--snapshot") is string snapshotPath)
        {
            // Только в памяти: в режиме снимка настройки не сохраняются.
            settings.Theme = Arg("--theme") ?? settings.Theme;
            settings.Language = Arg("--lang") ?? settings.Language;
            settings.DriverId = Arg("--driver") ?? settings.DriverId;
            LoadResources(settings);
            SettingsStore.AttachPresets(settings);
            ControlId? select = Enum.TryParse<ControlId>(Arg("--select"), out var id) ? id : null;
            if (select is ControlId selected && Enum.TryParse<ActionKind>(Arg("--kind"), out var kind))
            {
                settings.Profile.Layers[0].Get(selected).Kind = kind;
                settings.EnsureTriggers();
            }
            var snapshotForm = new MainForm(settings, false, null, snapshotPath, select);
            if (int.TryParse(Arg("--tour"), out int tourStep)) snapshotForm.SnapshotTourStep = tourStep;
            Application.Run(snapshotForm);
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
        // Пока открыта программа, клавиши обслуживает она, а фоновая служба ждёт.
        Agent.OmkService.RequestPause();
        Agent.OmkService.RefreshPathIfInstalled();
        LoadResources(settings);
        SettingsStore.AttachPresets(settings);

        bool startHidden = args.Contains("--tray");
        if (!settings.SetupDone && !startHidden) RunSetup(settings);

        ReopenState? reopen = null;
        while (true)
        {
            var form = new MainForm(settings, startHidden, showEvent, reopen: reopen);
            Application.Run(form);
            if (form.Reopen == null) break;

            // Смена темы или языка, мастер настройки: перечитать ресурсы и открыть окно заново.
            reopen = form.Reopen;
            startHidden = false;
            if (!settings.SetupDone)
            {
                RunSetup(settings);
                reopen = reopen with { Selected = settings.Controls[0], Layer = 0 };
            }
            Notices.Clear();
            LoadResources(settings);
            settings.Normalize();
        }
        return 0;
    }

    /// <summary>Снимок окна для проверки вёрстки: показать, сохранить картинку, закрыть.</summary>
    static void SnapshotForm(Form form, string path)
    {
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Shown += async (_, _) =>
        {
            await Task.Delay(600);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        };
        Application.Run(form);
    }

    /// <summary>Невидимое окно, которое открывает модальный диалог — чтобы снять и его.</summary>
    sealed class FormShot : Form
    {
        readonly Action _show;
        public Form? Dialog { get; private set; }

        public FormShot(Action show)
        {
            _show = show;
            ShowInTaskbar = false;
            Opacity = 0;
            Size = new Size(10, 10);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Application.Idle += CatchDialog;
            BeginInvoke(_show);
        }

        void CatchDialog(object? sender, EventArgs e)
        {
            Dialog ??= Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != this && f.Visible);
            if (Dialog == null) return;
            Application.Idle -= CatchDialog;
            var dialog = Dialog;
            _ = Task.Delay(500).ContinueWith(_ => BeginInvoke(() =>
            {
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                bitmap.Save(SnapshotPath!, System.Drawing.Imaging.ImageFormat.Png);
                dialog.Close();
                Close();
            }));
        }

        public string? SnapshotPath { get; set; }
    }

    /// <summary>Мастер первого запуска: язык, тема, плата. Выбранное сохраняется сразу.</summary>
    static void RunSetup(AppSettings settings)
    {
        Application.Run(new SetupWizardForm(settings));
        settings.SetupDone = true;
        try
        {
            SettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не сохранилось — мастер покажется ещё раз при следующем запуске.
        }
        Notices.Clear();
        LoadResources(settings);
        settings.Normalize();
    }

    /// <summary>
    /// Языки, драйверы и темы из папок рядом с программой. Если каких-то файлов нет, работают встроенные
    /// настройки, а в окне появится уведомление.
    /// </summary>
    static void LoadResources(AppSettings settings)
    {
        L.Load(settings.Language);
        DriverStore.Load();
        ThemeStore.Load();
        ThemeStore.Apply(settings.Theme);
        KeyCatalog.Rebuild();
        MediaCatalog.Rebuild();
    }
}
