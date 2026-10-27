using OshiMakiKey.Model;
using OshiMakiKey.UI;

namespace OshiMakiKey;

/// <summary>
/// Точка входа. Обычный запуск — одна копия программы с одним окном (повторный запуск лишь показывает его);
/// --service — фоновая служба без окна; остальные ключи — режимы проверки без участия пользователя.
/// </summary>
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
    ///                        --switch-theme id / --switch-lang код — сменить уже открытому окну (проверка без перезапуска),
    ///                        --page settings|about|setup — снимок с открытой страницей
    ///   --sky-hour 0..23     час для неба анимированных тем вместо часов компьютера
    ///   --form-snapshot settings0..6|about|setup-0..2|overwrite файл.png  снимок одной страницы или диалога
    ///   --drop-test файл     перетаскивание действий на клавиши и ручку, отчёт PASS/FAIL в файл
    ///   --wizard-snapshot файл.png  снимок страницы проверки записи
    ///   --monitor-snapshot файл.png снимок монитора с примерными строками
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

        // Служба Windows и её установка (последние два — уже с правами администратора, их запускает UAC из настроек).
        if (args.Contains("--winservice")) return Agent.WinService.Run();
        if (args.Contains("--install-winservice")) return Agent.WinService.Install();
        if (args.Contains("--remove-winservice")) return Agent.WinService.Remove();

        if (Arg("--selftest") is string testReport) return SelfTest.Run(testReport);
        if (Arg("--probe") is string probeReport) return SelfTest.Probe(probeReport);

        if (Arg("--export-icon") is string iconPath)
        {
            File.WriteAllBytes(iconPath, Theme.BuildIconBytes());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Theme.Init();
        if (int.TryParse(Arg("--sky-hour"), out int skyHour)) ThemeBackdrop.HourOverride = skyHour;

        // Фоновая служба OMK_Studio-Service — тот же exe без окна.
        if (args.Contains("--service")) return Agent.OmkService.Run();

        var settings = SettingsStore.LoadSettings();

        if (Arg("--monitor-snapshot") is string monitorShot)
        {
            LoadResources(settings);
            var monitor = new MonitorForm();
            monitor.Shown += (_, _) => monitor.AddSamples();
            SnapshotForm(monitor, monitorShot);
            return 0;
        }

        if (Arg("--wizard-snapshot") is string wizardShot)
        {
            LoadResources(settings);
            SettingsStore.AttachPresets(settings);
            SnapshotForm(PageHost(new WriteCheckPage(settings.EffectiveDriver)), wizardShot);
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
                "about" => PageHost(new AboutPage(settings, null)),
                "setup-0" or "setup-1" or "setup-2" => PageHost(new SetupPage(settings, formName[^1] - '0')),
                "overwrite" => new FormShot(() => ThemedDialog.Show(null, Branding.AppName, L.T("drop.overwrite.heading", "Внимание, вы хотите перезаписать комбинацию на клавише!"),
                    L.F("drop.overwrite.text", "Сейчас на «{0}»: {1}\nБудет: {2}", "Клавиша 4", "Ctrl + V", "Предыдущий трек"),
                    new[] { L.T("drop.overwrite", "Перезаписать"), L.T("button.cancel", "Отмена") }, DialogKind.Warning, L.T("drop.dontAsk", "Больше не показывать это сообщение"), 1)),
                _ when formName.StartsWith("settings") => PageHost(new SettingsPage(settings, null, formName.Length > 8 ? formName[8] - '0' : 0)),
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

        if (Arg("--drop-test") is string dropReport)
        {
            LoadResources(settings);
            SettingsStore.AttachPresets(settings);
            var form = new MainForm(settings, false, null, dropReport);
            form.Shown += (_, _) =>
            {
                File.WriteAllLines(dropReport, form.RunDropTest());
                form.Close();
            };
            Application.Run(form);
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
            snapshotForm.SnapshotSwitch = (Arg("--switch-theme"), Arg("--switch-lang"));
            snapshotForm.SnapshotPage = Arg("--page");
            snapshotForm.SnapshotPreviewTheme = Arg("--preview-theme");
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
        LogFiles.Cleanup();

        // Одно окно на всё время работы: мастер первого запуска, настройки и смена темы или языка — внутри него.
        Application.Run(new MainForm(settings, args.Contains("--tray"), showEvent));
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

    /// <summary>Окно размером с главное, в котором показана одна страница (для снимков вёрстки).</summary>
    static Form PageHost(Control page)
    {
        var host = new Form { Text = Branding.AppName, BackColor = Theme.Window, AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(Theme.S(1180), Theme.S(760)) };
        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        Theme.ApplyTo(host);
        return host;
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

    /// <summary>
    /// Языки, драйверы и темы из папок рядом с программой. Если каких-то файлов нет, работают встроенные
    /// настройки, а в окне появится уведомление.
    /// </summary>
    internal static void LoadResources(AppSettings settings)
    {
        L.Load(settings.Language);
        DriverStore.Load();
        ThemeStore.Load();
        ThemeStore.Apply(settings.Theme);
        KeyCatalog.Rebuild();
        MediaCatalog.Rebuild();
    }
}
