using System.Runtime.InteropServices;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Low-level mouse and keyboard hooks used only while the mirror window is in the
/// foreground: Alt + wheel zooms the PC view, and a handful of app hotkeys (F11, Escape,
/// Ctrl+Alt+P/C, calibration keys) are intercepted before scrcpy can forward them to the phone.
/// </summary>
public sealed class InputHooks : IDisposable
{
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private readonly HashSet<int> _consumedKeys = [];

    public InputHooks()
    {
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    /// <summary>Must return true when the wheel event should be consumed. Args: delta, screen x, screen y.</summary>
    public Func<int, int, int, bool>? AltWheel { get; set; }
    public Func<int, int, int, bool>? PanelWheel { get; set; }

    /// <summary>Must return true when the key should be consumed. Args: virtual key, ctrl, alt, shift.</summary>
    public Func<int, bool, bool, bool, bool>? KeyDown { get; set; }

    /// <summary>Tells the hooks whether our window currently owns the keyboard (foreground and not minimized).</summary>
    public Func<bool>? IsActive { get; set; }

    public void Install()
    {
        var module = NativeMethods.GetModuleHandleW(null);
        if (_mouseHook == IntPtr.Zero)
        {
            _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        }

        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        }
    }

    public void Uninstall()
    {
        _consumedKeys.Clear();
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt64() == NativeMethods.WM_MOUSEWHEEL && IsActive?.Invoke() == true)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((data.mouseData >> 16) & 0xFFFF);
            if (PanelWheel?.Invoke(delta, data.pt.X, data.pt.Y) == true ||
                (NativeMethods.IsKeyDown(NativeMethods.VK_MENU) && AltWheel?.Invoke(delta, data.pt.X, data.pt.Y) == true))
            {
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var message = wParam.ToInt64();
        if (nCode >= 0 && message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            var released = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (_consumedKeys.Remove((int)released.vkCode)) return new IntPtr(1);
        }
        if (nCode >= 0 && message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN && IsActive?.Invoke() == true)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (_consumedKeys.Contains((int)data.vkCode)) return new IntPtr(1);
            var ctrl = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
            var alt = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
            var shift = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);
            if (KeyDown?.Invoke((int)data.vkCode, ctrl, alt, shift) == true)
            {
                _consumedKeys.Add((int)data.vkCode);
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
