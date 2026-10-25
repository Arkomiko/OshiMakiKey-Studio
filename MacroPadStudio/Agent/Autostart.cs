using Microsoft.Win32;

namespace OshiMakiKey.Agent;

/// <summary>Автозапуск через HKCU\...\Run — без прав администратора, только для текущего пользователя.</summary>
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = Branding.InternalName;

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Portable-программу могут перенести в другую папку — тогда обновляем путь в автозапуске.</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && current != Command) key.SetValue(ValueName, Command);
        }
        catch (Exception)
        {
            // Нет доступа к реестру — автозапуск просто останется как был.
        }
    }
}
