using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace OshiMakiKey.Agent;

/// <summary>
/// Настоящая служба Windows OMK_Studio-Service (видна в «Службах»). Службы работают в сеансе 0 без доступа к клавиатуре,
/// звуку и окнам пользователя, поэтому сама служба действий не выполняет: она следит, чтобы в сеансе вошедшего
/// пользователя работал помощник — тот же exe с ключом --service (<see cref="OmkService"/>), — и перезапускает его
/// при входе в Windows, разблокировке и сбое. Установка и удаление требуют прав администратора (запрос UAC).
/// </summary>
internal static class WinService
{
    public const string Name = OmkService.Name;
    const string ServiceArg = "--winservice";

    /// <summary>Служба зарегистрирована в Windows.</summary>
    public static bool IsInstalled => Status != null;

    /// <summary>Состояние службы (null — не установлена).</summary>
    public static ServiceControllerStatus? Status
    {
        get
        {
            try
            {
                using var controller = new ServiceController(Name);
                return controller.Status;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Exe, на который указывает служба (папку программы могли перенести — тогда службу нужно переустановить).</summary>
    public static string? ImagePath
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{Name}");
            return key?.GetValue("ImagePath") as string;
        }
    }

    static string Command => $"\"{Environment.ProcessPath}\" {ServiceArg}";

    public static bool PathMatches => string.Equals(ImagePath, Command, StringComparison.OrdinalIgnoreCase);

    /// <summary>Запускает этот же exe с правами администратора (запрос UAC) для установки или удаления службы.</summary>
    public static bool RunElevated(string argument)
    {
        if (Environment.ProcessPath is not string exe) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, argument) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            process?.WaitForExit(30000);
            return process?.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // Пользователь отказался в окне UAC.
            return false;
        }
    }

    /// <summary>--install-winservice (уже с правами администратора): регистрирует, настраивает перезапуск при сбое и запускает.</summary>
    public static int Install()
    {
        Sc("stop", Name);
        Sc("delete", Name);
        Thread.Sleep(500);
        int code = Sc("create", Name, "binPath=", Command, "start=", "auto", "DisplayName=", "OMK Studio Service");
        if (code != 0) return code;
        Sc("description", Name, "OshiMakiKey Studio: выполняет действия кнопок макро-клавиатуры, когда окно программы закрыто.");
        Sc("failure", Name, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");
        return Sc("start", Name);
    }

    /// <summary>--remove-winservice (уже с правами администратора): останавливает и удаляет службу.</summary>
    public static int Remove()
    {
        Sc("stop", Name);
        return Sc("delete", Name);
    }

    static int Sc(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info);
        if (process == null) return -1;
        process.WaitForExit(15000);
        return process.ExitCode;
    }

    /// <summary>Режим --winservice: запуск под управлением диспетчера служб.</summary>
    public static int Run()
    {
        ServiceBase.Run(new Host());
        return 0;
    }

    /// <summary>Сама служба: держит помощника запущенным в сеансе активного пользователя.</summary>
    sealed class Host : ServiceBase
    {
        readonly Dictionary<uint, Process> _helpers = new();
        System.Threading.Timer? _timer;

        public Host()
        {
            ServiceName = Name;
            CanHandleSessionChangeEvent = true;
            CanStop = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args) => _timer = new System.Threading.Timer(_ => Ensure(), null, 0, 10000);

        protected override void OnSessionChange(SessionChangeDescription change)
        {
            if (change.Reason is SessionChangeReason.SessionLogon or SessionChangeReason.ConsoleConnect or SessionChangeReason.SessionUnlock)
                Ensure();
        }

        protected override void OnStop()
        {
            _timer?.Dispose();
            lock (_helpers)
            {
                // Помощнику нечего сохранять — просто завершаем его вместе со службой.
                foreach (var helper in _helpers.Values)
                {
                    try { if (!helper.HasExited) helper.Kill(); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                    helper.Dispose();
                }
                _helpers.Clear();
            }
        }

        /// <summary>В активном сеансе есть вошедший пользователь, а помощника нет (или он упал) — запускаем.</summary>
        void Ensure()
        {
            lock (_helpers)
            {
                uint session = WTSGetActiveConsoleSessionId();
                if (session == 0xFFFFFFFF) return;
                if (_helpers.TryGetValue(session, out var running))
                {
                    if (!running.HasExited) return;
                    running.Dispose();
                    _helpers.Remove(session);
                }
                if (StartInSession(session) is { } helper) _helpers[session] = helper;
            }
        }

        static Process? StartInSession(uint session)
        {
            if (Environment.ProcessPath is not string exe || !WTSQueryUserToken(session, out var token)) return null;
            IntPtr environment = IntPtr.Zero;
            try
            {
                CreateEnvironmentBlock(out environment, token, false);
                var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
                var command = new StringBuilder($"\"{exe}\" --service");
                const uint CreateUnicodeEnvironment = 0x00000400, CreateNoWindow = 0x08000000;
                if (!CreateProcessAsUser(token, null, command, IntPtr.Zero, IntPtr.Zero, false, CreateUnicodeEnvironment | CreateNoWindow,
                        environment, Path.GetDirectoryName(exe), ref startup, out var info))
                    return null;
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
                try { return Process.GetProcessById(info.dwProcessId); }
                catch (ArgumentException) { return null; }
            }
            finally
            {
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                CloseHandle(token);
            }
        }
    }

    // ───────────────────────── WinAPI ─────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
