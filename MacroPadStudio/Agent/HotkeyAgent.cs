using OshiMakiKey.Model;

namespace OshiMakiKey.Agent;

/// <summary>
/// Фоновый «драйвер» системных действий: регистрирует служебные клавиши (F13–F24…) как глобальные
/// горячие клавиши. Windows не передаёт их активному окну, а присылает нам WM_HOTKEY.
/// </summary>
internal sealed class HotkeyAgent : NativeWindow, IDisposable
{
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly HashSet<int> _registered = new();

    public event Action<int>? Triggered;

    public HotkeyAgent() => CreateHandle(new CreateParams { Caption = "OshiMakiKey.Hotkeys", Parent = HWND_MESSAGE });

    /// <summary>Регистрирует ровно этот набор служебных клавиш. Возвращает те, что заняты другой программой.</summary>
    public List<int> Apply(IEnumerable<int> slots)
    {
        foreach (int slot in _registered) Win32.UnregisterHotKey(Handle, slot + 1);
        _registered.Clear();

        var failed = new List<int>();
        foreach (int slot in slots.Distinct().OrderBy(s => s))
        {
            uint modifiers = Win32.MOD_NOREPEAT
                | (TriggerKeys.HasShift(slot) ? Win32.MOD_SHIFT : 0)
                | (TriggerKeys.HasCtrl(slot) ? Win32.MOD_CONTROL : 0);
            if (Win32.RegisterHotKey(Handle, slot + 1, modifiers, (uint)TriggerKeys.VirtualKey(slot)))
                _registered.Add(slot);
            else
                failed.Add(slot);
        }
        return failed;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY)
        {
            Triggered?.Invoke(m.WParam.ToInt32() - 1);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        Apply(Array.Empty<int>());
        DestroyHandle();
    }
}
