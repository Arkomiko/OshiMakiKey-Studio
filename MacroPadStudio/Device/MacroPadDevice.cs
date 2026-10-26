using System.Runtime.InteropServices;
using OshiMakiKey.Model;
using Microsoft.Win32.SafeHandles;
using static OshiMakiKey.Device.NativeMethods;

namespace OshiMakiKey.Device;

/// <summary>Открытый служебный интерфейс макро-клавиатуры, через который пишется конфигурация.</summary>
public sealed class MacroPadDevice : IDisposable
{
    /// <summary>USB ID плат, которые знают драйверы.</summary>
    public static IReadOnlyList<(ushort Vid, ushort Pid)> Supported => DriverStore.SupportedIds;

    const int WriteTimeoutMs = 1000;

    readonly SafeFileHandle _handle;

    public HidDeviceInfo Info { get; }

    MacroPadDevice(SafeFileHandle handle, HidDeviceInfo info)
    {
        _handle = handle;
        Info = info;
    }

    /// <summary>Ищет служебный интерфейс: у него выходной отчёт 64 байта (+1 байт Report ID).</summary>
    public static HidDeviceInfo? FindConfigInterface()
    {
        foreach (var (vid, pid) in Supported)
            foreach (var info in HidEnumerator.Enumerate(vid, pid))
                if (info.OutputReportLength == K8890Protocol.PacketSize + 1)
                    return info;
        return null;
    }

    /// <summary>
    /// Подбирает драйвер подключённой плате: по USB ID и виду мультимедиа-интерфейса — битовый (только кнопки,
    /// без значений) у CH552 6 клавиш + ручка, 16-битный код у моделей формата ch57x. Null — плата не найдена.
    /// </summary>
    public static DriverDef? DetectDriver()
    {
        foreach (var (vid, pid) in Supported)
        {
            var consumer = HidEnumerator.Enumerate(vid, pid).FirstOrDefault(d => d.UsagePage == 0x0C);
            if (consumer != null && DriverStore.Match(vid, pid, consumer.InputValueCaps == 0) is { } driver) return driver;
        }
        return null;
    }

    public static MacroPadDevice Open(HidDeviceInfo info)
    {
        var handle = CreateFile(info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException(L.F("device.openError", "Не удалось открыть устройство (код ошибки {0}).", error));
        }
        return new MacroPadDevice(handle, info);
    }

    public void SendInit() => SendPacket(K8890Protocol.InitPacket());

    /// <summary>
    /// Отправляет кадры через interrupt OUT (WriteFile). SET_REPORT по управляющему каналу эта прошивка
    /// отклоняет (STALL, код 31), поэтому другого пути нет.
    /// </summary>
    public void Write(IReadOnlyList<byte[]> packets, bool sendInit, IProgress<int>? progress = null)
    {
        if (sendInit) SendInit();
        for (int i = 0; i < packets.Count; i++)
        {
            SendPacket(packets[i]);
            // Прошивке нужна пауза между кадрами, особенно перед записью во флеш.
            Thread.Sleep(10);
            progress?.Report((i + 1) * 100 / packets.Count);
        }
    }

    public void SendPacket(byte[] packet)
    {
        // Report ID = 0 (у интерфейса нет нумерованных отчётов), затем 64 байта данных.
        var report = new byte[Info.OutputReportLength];
        Array.Copy(packet, 0, report, 1, Math.Min(packet.Length, report.Length - 1));
        WriteReport(report);
    }

    void WriteReport(byte[] report)
    {
        using var completed = new ManualResetEvent(false);
        IntPtr buffer = Marshal.AllocHGlobal(report.Length);
        IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
        try
        {
            Marshal.Copy(report, 0, buffer, report.Length);
            Marshal.StructureToPtr(new OVERLAPPED { hEvent = completed.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);

            if (!WriteFile(_handle, buffer, report.Length, IntPtr.Zero, overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_IO_PENDING)
                    throw new IOException(L.F("device.writeError", "Ошибка записи в устройство (код {0}).", error));

                if (!completed.WaitOne(WriteTimeoutMs))
                {
                    CancelIoEx(_handle, overlapped);
                    GetOverlappedResult(_handle, overlapped, out _, true);
                    throw new TimeoutException(L.T("device.timeout", "Устройство не отвечает (истекло время записи)."));
                }
            }

            if (!GetOverlappedResult(_handle, overlapped, out int written, false))
                throw new IOException(L.F("device.writeError", "Ошибка записи в устройство (код {0}).", Marshal.GetLastWin32Error()));
            if (written != report.Length)
                throw new IOException(L.F("device.partial", "Записано {0} из {1} байт.", written, report.Length));
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => _handle.Dispose();
}
