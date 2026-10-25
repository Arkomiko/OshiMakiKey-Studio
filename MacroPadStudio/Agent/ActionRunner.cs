using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OshiMakiKey.Model;

namespace OshiMakiKey.Agent;

/// <summary>Выполняет действия, которые устройство само сделать не может.</summary>
internal static class ActionRunner
{
    public static async Task ExecuteAsync(PadAction action, Form owner)
    {
        switch (action.Kind)
        {
            case ActionKind.Keys:
                await WaitForModifiersReleasedAsync();
                InputSender.SendChords(action.Chords.Where(c => !c.IsEmpty));
                return;
            case ActionKind.Media:
                await WaitForModifiersReleasedAsync();
                await MediaSender.SendAsync(action.MediaCode);
                return;
            case ActionKind.Mouse:
                await WaitForModifiersReleasedAsync();
                InputSender.SendMouse(action);
                return;
            case ActionKind.System:
                await ExecuteSystemAsync(action, owner);
                return;
        }
    }

    static async Task ExecuteSystemAsync(PadAction action, Form owner)
    {
        switch (action.SystemAction)
        {
            case SystemActionKind.RunProgram:
            {
                string path = Required(action.Target, "Не выбрана программа для запуска.");
                var info = new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Arguments = Environment.ExpandEnvironmentVariables(action.Arguments),
                };
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) info.WorkingDirectory = folder;
                Start(info);
                break;
            }
            case SystemActionKind.OpenUrl:
                Start(new ProcessStartInfo(NormalizeUrl(Required(action.Target, "Не указан адрес сайта."))) { UseShellExecute = true });
                break;
            case SystemActionKind.OpenPath:
                Start(new ProcessStartInfo(Required(action.Target, "Не указан файл или папка.")) { UseShellExecute = true });
                break;
            case SystemActionKind.TypeText:
                if (action.Text.Length == 0) return;
                await WaitForModifiersReleasedAsync();
                InputSender.TypeText(action.Text);
                break;
            case SystemActionKind.RunCommand:
            {
                string command = action.Target.Trim();
                if (command.Length == 0) throw new InvalidOperationException("Не указана команда.");
                Start(new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                });
                break;
            }
            case SystemActionKind.LockPc:
                if (!Win32.LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                break;
            case SystemActionKind.MonitorOff:
                // Небольшая пауза, чтобы отпускание клавиши не разбудило экран сразу же.
                await Task.Delay(600);
                Win32.SendMessage(owner.Handle, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MONITORPOWER, (IntPtr)2);
                break;
            case SystemActionKind.Sleep:
                await Task.Delay(300);
                Application.SetSuspendState(PowerState.Suspend, false, false);
                break;
            case SystemActionKind.Shutdown:
                if (Confirm("Выключить компьютер?")) RunShutdown("/s /t 0");
                break;
            case SystemActionKind.Restart:
                if (Confirm("Перезагрузить компьютер?")) RunShutdown("/r /t 0");
                break;
        }
    }

    static void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();

    static string Required(string value, string error)
    {
        string cleaned = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (cleaned.Length == 0) throw new InvalidOperationException(error);
        return cleaned;
    }

    static string NormalizeUrl(string url) =>
        url.Contains("://") || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;

    static bool Confirm(string question) =>
        MessageBox.Show(question, Branding.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly) == DialogResult.Yes;

    static void RunShutdown(string arguments) =>
        Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });

    static async Task WaitForModifiersReleasedAsync()
    {
        // Служебная клавиша может прийти с Ctrl/Shift — эмулировать ввод можно только после их отпускания.
        for (int i = 0; i < 100 && InputSender.AnyModifierDown(); i++) await Task.Delay(15);
    }
}

/// <summary>Эмуляция клавиатуры и мыши через SendInput.</summary>
internal static class InputSender
{
    static readonly int[] Modifiers = { Win32.VK_SHIFT, Win32.VK_CONTROL, Win32.VK_MENU, Win32.VK_LWIN, Win32.VK_RWIN };

    // Клавиши, которым нужен флаг «расширенная» (иначе стрелки и т.п. превращаются в цифровой блок).
    static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
        Keys.Left, Keys.Up, Keys.Right, Keys.Down, Keys.Divide, Keys.NumLock, Keys.Apps,
        Keys.PrintScreen, Keys.LWin, Keys.RWin, Keys.RControlKey, Keys.RMenu,
    };

    public static bool AnyModifierDown() => Modifiers.Any(vk => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>Нажимает сочетания по очереди: Ctrl + C, затем Ctrl + V…</summary>
    public static void SendChords(IEnumerable<KeyChord> chords)
    {
        var inputs = new List<Win32.INPUT>();
        foreach (var chord in chords)
        {
            var modifiers = new List<Keys>();
            if (chord.Ctrl) modifiers.Add(Keys.LControlKey);
            if (chord.Shift) modifiers.Add(Keys.LShiftKey);
            if (chord.Alt) modifiers.Add(Keys.LMenu);
            if (chord.Win) modifiers.Add(Keys.LWin);

            foreach (var modifier in modifiers) inputs.Add(VirtualKey(modifier, up: false));
            var key = KeyCatalog.VirtualKeyOf(chord.Key);
            if (key != Keys.None)
            {
                inputs.Add(VirtualKey(key, up: false));
                inputs.Add(VirtualKey(key, up: true));
            }
            for (int i = modifiers.Count - 1; i >= 0; i--) inputs.Add(VirtualKey(modifiers[i], up: true));
        }
        Send(inputs);
    }

    /// <summary>Одна виртуальная клавиша (мультимедиа, кнопки браузера и т.п.).</summary>
    public static void TapKey(Keys key) => Send(new List<Win32.INPUT> { VirtualKey(key, false), VirtualKey(key, true) });

    public static void SendMouse(PadAction action)
    {
        var inputs = new List<Win32.INPUT>();
        Keys modifier = action.MouseModifier switch
        {
            MouseModifier.Ctrl => Keys.LControlKey,
            MouseModifier.Shift => Keys.LShiftKey,
            MouseModifier.Alt => Keys.LMenu,
            _ => Keys.None,
        };
        if (modifier != Keys.None) inputs.Add(VirtualKey(modifier, up: false));

        int wheel = Win32.WHEEL_DELTA * Math.Clamp(action.WheelStep, 1, 20);
        switch (action.Mouse)
        {
            case MouseActionKind.LeftClick:
                inputs.Add(Mouse(Win32.MOUSEEVENTF_LEFTDOWN));
                inputs.Add(Mouse(Win32.MOUSEEVENTF_LEFTUP));
                break;
            case MouseActionKind.RightClick:
                inputs.Add(Mouse(Win32.MOUSEEVENTF_RIGHTDOWN));
                inputs.Add(Mouse(Win32.MOUSEEVENTF_RIGHTUP));
                break;
            case MouseActionKind.MiddleClick:
                inputs.Add(Mouse(Win32.MOUSEEVENTF_MIDDLEDOWN));
                inputs.Add(Mouse(Win32.MOUSEEVENTF_MIDDLEUP));
                break;
            case MouseActionKind.WheelUp:
                inputs.Add(Mouse(Win32.MOUSEEVENTF_WHEEL, wheel));
                break;
            case MouseActionKind.WheelDown:
                inputs.Add(Mouse(Win32.MOUSEEVENTF_WHEEL, -wheel));
                break;
        }

        if (modifier != Keys.None) inputs.Add(VirtualKey(modifier, up: true));
        Send(inputs);
    }

    /// <summary>Печатает текст как Unicode-ввод — работает с любой раскладкой и языком.</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<Win32.INPUT>();
        foreach (char ch in text.ReplaceLineEndings("\n"))
        {
            if (ch == '\n')
            {
                inputs.Add(VirtualKey(Keys.Enter, false));
                inputs.Add(VirtualKey(Keys.Enter, true));
            }
            else if (ch == '\t')
            {
                inputs.Add(VirtualKey(Keys.Tab, false));
                inputs.Add(VirtualKey(Keys.Tab, true));
            }
            else
            {
                inputs.Add(Keyboard(0, ch, Win32.KEYEVENTF_UNICODE));
                inputs.Add(Keyboard(0, ch, Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP));
            }
        }
        Send(inputs);
    }

    static void Send(List<Win32.INPUT> inputs)
    {
        int size = Marshal.SizeOf<Win32.INPUT>();
        foreach (var chunk in inputs.Chunk(64))
        {
            Win32.SendInput((uint)chunk.Length, chunk, size);
            if (inputs.Count > 64) Thread.Sleep(2);
        }
    }

    static Win32.INPUT VirtualKey(Keys key, bool up)
    {
        ushort vk = (ushort)key;
        ushort scan = (ushort)Win32.MapVirtualKey(vk, 0);
        uint flags = (up ? Win32.KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(key) ? Win32.KEYEVENTF_EXTENDEDKEY : 0);
        return Keyboard(vk, scan, flags);
    }

    static Win32.INPUT Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    static Win32.INPUT Mouse(uint flags, int wheel = 0) => new()
    {
        type = Win32.INPUT_MOUSE,
        U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)wheel) } },
    };
}

/// <summary>Мультимедийные клавиши: через виртуальные коды Windows, яркость — через WMI.</summary>
internal static class MediaSender
{
    static readonly Dictionary<ushort, Keys> VirtualKeys = new()
    {
        [0xCD] = Keys.MediaPlayPause,
        [0xB5] = Keys.MediaNextTrack,
        [0xB6] = Keys.MediaPreviousTrack,
        [0xB7] = Keys.MediaStop,
        [0xE9] = Keys.VolumeUp,
        [0xEA] = Keys.VolumeDown,
        [0xE2] = Keys.VolumeMute,
        [0x192] = Keys.LaunchApplication2,
        [0x194] = Keys.LaunchApplication1,
        [0x18A] = Keys.LaunchMail,
        [0x183] = Keys.SelectMedia,
        [0x221] = Keys.BrowserSearch,
        [0x223] = Keys.BrowserHome,
        [0x224] = Keys.BrowserBack,
        [0x225] = Keys.BrowserForward,
        [0x227] = Keys.BrowserRefresh,
        [0x22A] = Keys.BrowserFavorites,
    };

    public static async Task SendAsync(ushort code)
    {
        if (code is 0x6F or 0x70)
        {
            int delta = code == 0x6F ? 10 : -10;
            await Task.Run(() => ChangeBrightness(delta));
            return;
        }
        if (!VirtualKeys.TryGetValue(code, out var key))
            throw new InvalidOperationException($"Мультимедийная клавиша 0x{code:X3} не поддерживается.");
        InputSender.TapKey(key);
    }

    /// <summary>Меняет яркость встроенного экрана (ноутбук). Внешние мониторы WMI обычно не поддерживают.</summary>
    static void ChangeBrightness(int delta)
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new InvalidOperationException("WMI недоступен.");
            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic service = locator.ConnectServer(".", @"root\WMI");
            dynamic levels = service.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            dynamic methods = service.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            if ((int)levels.Count == 0 || (int)methods.Count == 0) throw new InvalidOperationException();

            int current = Convert.ToInt32(levels.ItemIndex(0).CurrentBrightness);
            methods.ItemIndex(0).WmiSetBrightness(1, (byte)Math.Clamp(current + delta, 0, 100));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException("Не удалось изменить яркость: Windows позволяет это только для встроенного экрана ноутбука.", ex);
        }
    }
}
