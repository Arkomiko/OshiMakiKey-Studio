using System.Runtime.InteropServices;

namespace OshiMakiKey.Agent;

/// <summary>Событие ввода, пришедшее от конкретного устройства (Raw Input).</summary>
internal abstract record RawEvent(IntPtr Device);

internal sealed record RawKeyboardEvent(IntPtr Device, Keys Key, ushort MakeCode, bool Up, bool Extended) : RawEvent(Device);

internal sealed record RawMouseEvent(IntPtr Device, ushort ButtonFlags, short WheelDelta, int X, int Y) : RawEvent(Device);

internal sealed record RawHidEvent(IntPtr Device, ushort UsagePage, ushort[] Usages, byte[] Report) : RawEvent(Device);

/// <summary>
/// Raw Input: видит ввод от каждого устройства отдельно и даже когда окно не активно.
/// Используется «Монитором нажатий», чтобы показать, что реально отправляет макро-клавиатура.
/// </summary>
internal static class RawInput
{
    public const int WM_INPUT = 0x00FF;

    const uint RID_INPUT = 0x10000003;
    const uint RIDI_DEVICENAME = 0x20000007;
    const uint RIDI_PREPARSEDDATA = 0x20000005;
    const uint RIDEV_REMOVE = 0x00000001;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RIM_TYPEMOUSE = 0, RIM_TYPEKEYBOARD = 1, RIM_TYPEHID = 2;
    const ushort RI_KEY_BREAK = 1, RI_KEY_E0 = 2;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr rawInput, uint command, byte[]? data, ref uint size, uint headerSize);

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);

    /// <summary>Имена всех устройств, которые видит Raw Input.</summary>
    public static List<string> DeviceNames()
    {
        uint count = 0, size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, size);
        var list = new RAWINPUTDEVICELIST[count];
        if (count == 0 || GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return new List<string>();
        return list.Take((int)count).Select(d => DeviceName(d.Device)).ToList();
    }

    // Unicode обязательно: без него .NET вызывает ANSI-версию, и имя устройства читается «кашей».
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usageList,
        ref uint usageLength, IntPtr preparsedData, byte[] report, uint reportLength);

    static readonly (ushort Page, ushort Usage)[] Collections =
    {
        (0x01, 0x06), // клавиатура
        (0x01, 0x02), // мышь
        (0x0C, 0x01), // мультимедиа
        (0x01, 0x80), // системные кнопки (питание, сон)
    };

    static readonly Dictionary<IntPtr, string> Names = new();
    static readonly Dictionary<IntPtr, IntPtr> Preparsed = new();

    /// <summary>Подписывает окно на ввод от всех клавиатур, мышей и мультимедиа-устройств. Null — успех, иначе текст ошибки.</summary>
    public static string? Register(IntPtr window) => Apply(RIDEV_INPUTSINK, window);

    public static void Unregister() => Apply(RIDEV_REMOVE, IntPtr.Zero);

    static string? Apply(uint flags, IntPtr window)
    {
        var devices = Collections.Select(c => new RAWINPUTDEVICE { UsagePage = c.Page, Usage = c.Usage, Flags = flags, Target = window }).ToArray();
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>())
            ? null
            : Model.L.F("rawinput.error", "RegisterRawInputDevices: код ошибки {0}", Marshal.GetLastWin32Error());
    }

    public static string DeviceName(IntPtr device)
    {
        if (Names.TryGetValue(device, out var cached)) return cached;
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        string name = "";
        if (size > 0)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref size) != uint.MaxValue)
                    name = Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return Names[device] = name;
    }

    /// <summary>Разбирает сообщение WM_INPUT.</summary>
    public static RawEvent? Read(IntPtr lParam)
    {
        uint headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, headerSize);
        if (size == 0) return null;
        var data = new byte[size];
        if (GetRawInputData(lParam, RID_INPUT, data, ref size, headerSize) == uint.MaxValue) return null;

        uint type = BitConverter.ToUInt32(data, 0);
        IntPtr device = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(data, 8) : (IntPtr)BitConverter.ToInt32(data, 8);
        int body = (int)headerSize;

        switch (type)
        {
            case RIM_TYPEKEYBOARD:
            {
                ushort makeCode = BitConverter.ToUInt16(data, body);
                ushort flags = BitConverter.ToUInt16(data, body + 2);
                ushort vkey = BitConverter.ToUInt16(data, body + 6);
                return new RawKeyboardEvent(device, (Keys)vkey, makeCode, (flags & RI_KEY_BREAK) != 0, (flags & RI_KEY_E0) != 0);
            }
            case RIM_TYPEMOUSE:
            {
                ushort buttonFlags = BitConverter.ToUInt16(data, body + 4);
                short buttonData = BitConverter.ToInt16(data, body + 6);
                int x = BitConverter.ToInt32(data, body + 12), y = BitConverter.ToInt32(data, body + 16);
                return new RawMouseEvent(device, buttonFlags, buttonData, x, y);
            }
            case RIM_TYPEHID:
            {
                int reportSize = (int)BitConverter.ToUInt32(data, body);
                int count = (int)BitConverter.ToUInt32(data, body + 4);
                if (reportSize <= 0 || count <= 0) return null;
                var report = data.AsSpan(body + 8, Math.Min(reportSize, data.Length - body - 8)).ToArray();
                var (page, usages) = DecodeUsages(device, report);
                return new RawHidEvent(device, page, usages, report);
            }
        }
        return null;
    }

    /// <summary>Какие кнопки (usages) нажаты в отчёте мультимедиа/системного устройства.</summary>
    static (ushort Page, ushort[] Usages) DecodeUsages(IntPtr device, byte[] report)
    {
        IntPtr preparsed = GetPreparsed(device);
        if (preparsed == IntPtr.Zero) return (0, Array.Empty<ushort>());
        foreach (ushort page in new ushort[] { 0x0C, 0x01 })
        {
            var usages = new ushort[32];
            uint length = (uint)usages.Length;
            if (HidP_GetUsages(0, page, 0, usages, ref length, preparsed, report, (uint)report.Length) == HIDP_STATUS_SUCCESS && length > 0)
                return (page, usages.Take((int)length).ToArray());
        }
        return (0x0C, Array.Empty<ushort>());
    }

    static IntPtr GetPreparsed(IntPtr device)
    {
        if (Preparsed.TryGetValue(device, out var cached)) return cached;
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        IntPtr buffer = IntPtr.Zero;
        if (size > 0)
        {
            buffer = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, buffer, ref size) == uint.MaxValue)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
            }
        }
        // Кэш живёт до конца работы программы: устройств немного, память небольшая.
        return Preparsed[device] = buffer;
    }
}
