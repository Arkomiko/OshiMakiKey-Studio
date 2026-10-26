using System.Diagnostics;
using Microsoft.Win32;
using OshiMakiKey.Model;

namespace OshiMakiKey.Agent;

/// <summary>
/// Фоновая служба OMK_Studio-Service: тот же exe с ключом --service, без окна. Выполняет действия кнопок
/// (громкость, треки, системные действия…), когда окно программы закрыто. Это не служба Windows: те работают
/// в отдельном сеансе 0 и не получают нажатий пользователя, поэтому служба запускается при входе в Windows
/// (HKCU\...\Run). Пока открыта программа, служба отпускает служебные клавиши, а после её закрытия перечитывает
/// настройки и пресет и снова берёт их.
/// </summary>
internal static class OmkService
{
    public const string Name = "OMK_Studio-Service";

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string InstanceMutex = Name + ".Instance";
    const string PauseEventName = Name + ".Pause";
    const string PausedEventName = Name + ".Paused";
    const string StopEventName = Name + ".Stop";
    const string AppMutex = Branding.InternalName + ".SingleInstance";

    static string Command => $"\"{Environment.ProcessPath}\" --service";

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static bool IsRunning => Exists(InstanceMutex);

    static bool Exists(string mutexName)
    {
        if (!Mutex.TryOpenExisting(mutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>Автозапуск при входе в Windows и запуск прямо сейчас.</summary>
    public static void Install()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue(Name, Command);
        Start();
    }

    public static void Remove()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue(Name, throwOnMissingValue: false);
        Stop();
    }

    public static void Reinstall()
    {
        Remove();
        Install();
    }

    public static void Start()
    {
        if (IsRunning || Environment.ProcessPath is not string exe) return;
        Process.Start(new ProcessStartInfo(exe, "--service") { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
    }

    public static void Stop()
    {
        if (!IsRunning) return;
        if (EventWaitHandle.TryOpenExisting(StopEventName, out var stop))
            using (stop) stop.Set();
        for (int i = 0; i < 30 && IsRunning; i++) Thread.Sleep(100);
    }

    /// <summary>Программа запускается: просим службу отпустить служебные клавиши и ждём подтверждения.</summary>
    public static void RequestPause()
    {
        if (!IsRunning) return;
        try
        {
            using var paused = new EventWaitHandle(false, EventResetMode.AutoReset, PausedEventName);
            if (!EventWaitHandle.TryOpenExisting(PauseEventName, out var pause)) return;
            using (pause) pause.Set();
            paused.WaitOne(1500);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
        }
    }

    /// <summary>Exe перенесли в другую папку — обновляем путь в автозапуске.</summary>
    public static void RefreshPathIfInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(Name) is string current && current != Command) key.SetValue(Name, Command);
        }
        catch (Exception)
        {
            // Нет доступа к реестру — путь останется прежним.
        }
    }

    /// <summary>Режим --service.</summary>
    public static int Run()
    {
        using var mutex = new Mutex(true, InstanceMutex, out bool created);
        if (!created) return 0;
        using var context = new ServiceContext();
        Application.Run(context);
        return 0;
    }

    static bool AppIsRunning => Exists(AppMutex);

    sealed class ServiceContext : ApplicationContext
    {
        readonly Form _owner = new() { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0 };
        readonly HotkeyAgent _agent = new();
        readonly EventWaitHandle _pause = new(false, EventResetMode.AutoReset, PauseEventName);
        readonly EventWaitHandle _stop = new(false, EventResetMode.AutoReset, StopEventName);
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
        readonly RegisteredWaitHandle _pauseWait, _stopWait;
        readonly HoldFilter _holdFilter = new();
        List<(int Slot, ControlId Control, PadAction Action)> _actions = new();
        bool _paused = true;

        public ServiceContext()
        {
            _ = _owner.Handle;
            _agent.Triggered += OnTrigger;
            _pauseWait = ThreadPool.RegisterWaitForSingleObject(_pause, (_, _) => Post(Pause), null, Timeout.Infinite, false);
            _stopWait = ThreadPool.RegisterWaitForSingleObject(_stop, (_, _) => Post(ExitThread), null, Timeout.Infinite, false);
            _timer.Tick += (_, _) => Check();
            _timer.Start();
            Check();
        }

        void Post(Action action)
        {
            try { _owner.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        void Check()
        {
            bool app = AppIsRunning;
            if (app && !_paused) Pause();
            else if (!app && _paused) Resume();
        }

        void Pause()
        {
            _agent.Apply(Array.Empty<int>());
            _paused = true;
            try
            {
                if (EventWaitHandle.TryOpenExisting(PausedEventName, out var paused))
                    using (paused) paused.Set();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>Перечитывает настройки и пресет службы и регистрирует их служебные клавиши.</summary>
        void Resume()
        {
            try
            {
                var settings = SettingsStore.LoadSettings();
                L.Load(settings.Language);
                DriverStore.Load();
                KeyCatalog.Rebuild();
                MediaCatalog.Rebuild();
                Notices.Clear();
                if (settings.ServicePreset is string preset) settings.ActivePreset = preset;
                SettingsStore.AttachPresets(settings);
                _actions = settings.Profile.All(settings.EffectiveLayerCount, settings.Controls)
                    .Where(x => x.Action.TriggerSlot != null)
                    .Select(x => (x.Action.TriggerSlot!.Value, x.Control, x.Action))
                    .ToList();
                _agent.Apply(_actions.Select(a => a.Slot));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _actions = new();
            }
            _paused = false;
        }

        async void OnTrigger(int slot)
        {
            var (_, control, action) = _actions.FirstOrDefault(a => a.Slot == slot);
            if (action == null || !_holdFilter.ShouldRun(slot, control, action)) return;
            try
            {
                await ActionRunner.ExecuteAsync(action, _owner);
            }
            catch (Exception)
            {
                // Без окна сообщить об ошибке некуда; программа покажет её при проверке действия.
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _pauseWait.Unregister(null);
                _stopWait.Unregister(null);
                _agent.Dispose();
                _pause.Dispose();
                _stop.Dispose();
                _owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
