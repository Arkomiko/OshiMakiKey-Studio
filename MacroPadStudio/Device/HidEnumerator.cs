using System.Runtime.InteropServices;
using static OshiMakiKey.Device.NativeMethods;

namespace OshiMakiKey.Device;

public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    int InputValueCaps);

/// <summary>Перечисление HID-интерфейсов через SetupAPI.</summary>
public static class HidEnumerator
{
    public static List<HidDeviceInfo> Enumerate(ushort vendorId, ushort productId)
    {
        var result = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out var hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return result;

        string tag = $"vid_{vendorId:x4}&pid_{productId:x4}";
        try
        {
            for (int index = 0; ; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;

                string? path = GetDevicePath(set, ref data);
                if (path == null || !path.Contains(tag, StringComparison.OrdinalIgnoreCase)) continue;

                var info = Query(path);
                if (info != null && info.VendorId == vendorId && info.ProductId == productId) result.Add(info);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    /// <summary>Читает атрибуты и размеры отчётов. Открывает устройство без прав доступа — так можно даже для клавиатур.</summary>
    public static HidDeviceInfo? Query(string path)
    {
        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(handle, ref attributes)) return null;
        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS) return null;
            return new HidDeviceInfo(path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, caps.NumberInputValueCaps);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    static string? GetDevicePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize: 8 в 64-битном процессе, 6 — в 32-битном.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
