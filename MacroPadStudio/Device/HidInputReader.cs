using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static OshiMakiKey.Device.NativeMethods;

namespace OshiMakiKey.Device;

/// <summary>
/// Читает входные отчёты HID-интерфейса напрямую, в обход обработки Windows. Работает только для интерфейсов,
/// которые система не открыла монопольно (у макро-клавиатуры это служебный и мультимедиа-интерфейсы).
/// Отчёты приходят в фоновом потоке.
/// </summary>
public sealed class HidInputReader : IDisposable
{
    readonly SafeFileHandle _handle;
    readonly int _length;
    readonly Thread _thread;
    readonly ManualResetEvent _stop = new(false);

    public HidDeviceInfo Info { get; }

    public event Action<HidInputReader, byte[]>? ReportReceived;

    public HidInputReader(HidDeviceInfo info)
    {
        Info = info;
        _length = info.InputReportLength;
        _handle = CreateFile(info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new IOException($"Не удалось открыть интерфейс (код {error}).");
        }
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "HidInputReader" };
    }

    public void Start() => _thread.Start();

    void ReadLoop()
    {
        IntPtr buffer = Marshal.AllocHGlobal(_length);
        IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
        using var completed = new ManualResetEvent(false);
        try
        {
            while (!_stop.WaitOne(0))
            {
                completed.Reset();
                Marshal.StructureToPtr(new OVERLAPPED { hEvent = completed.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
                if (!ReadFile(_handle, buffer, _length, IntPtr.Zero, overlapped) && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                    return;

                if (WaitHandle.WaitAny(new WaitHandle[] { completed, _stop }) == 1)
                {
                    CancelIoEx(_handle, overlapped);
                    GetOverlappedResult(_handle, overlapped, out _, true);
                    return;
                }
                if (!GetOverlappedResult(_handle, overlapped, out int read, false)) return;

                var report = new byte[read];
                Marshal.Copy(buffer, report, 0, read);
                ReportReceived?.Invoke(this, report);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive) _thread.Join(1000);
        _handle.Dispose();
        _stop.Dispose();
    }
}
