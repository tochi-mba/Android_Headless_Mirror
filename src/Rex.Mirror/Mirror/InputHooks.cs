using System.Runtime.InteropServices;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Low-level mouse and keyboard hooks. While the mirror window is in the foreground, Alt + wheel
/// zooms the PC view and the app's own keys (F11, Escape, Ctrl+Alt+P/C, calibration keys) are
/// intercepted before scrcpy can forward them to the phone. Keys from anywhere are looked at
/// whatever is in front.
/// </summary>
public sealed class InputHooks : IDisposable
{
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private readonly KeyRepeat _taken = new();

    public InputHooks()
    {
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    /// <summary>Must return true when the wheel event should be consumed. Args: delta, screen x, screen y.</summary>
    public Func<int, int, int, bool>? AltWheel { get; set; }
    public Func<int, int, int, bool>? PanelWheel { get; set; }

    /// <summary>Must return true when the key should be consumed. Asked only while the window is in front.</summary>
    public Func<int, KeyMods, bool>? KeyDown { get; set; }

    /// <summary>
    /// Must return true when the key is a key from anywhere and should be consumed. Asked first, for
    /// every key-down whatever window is in front, except while AltGr is held.
    /// </summary>
    public Func<int, KeyMods, bool>? GlobalKey { get; set; }

    /// <summary>
    /// Must return true when a shortcut box is recording and takes the key (down or up); asked
    /// first, while the window is in front, so a key another app has claimed is recorded too.
    /// </summary>
    public Func<int, bool, bool>? Record { get; set; }

    /// <summary>When (Environment.TickCount64) the last key went on to the window in front of this app's own; null before the first.</summary>
    public long? LastKeyToPhone { get; private set; }

    /// <summary>Whether the keyboard hook is in place.</summary>
    public bool KeyboardInstalled => _keyboardHook != IntPtr.Zero;

    /// <summary>Tells the hooks whether our window currently owns the keyboard (foreground and not minimized).</summary>
    public Func<bool>? IsActive { get; set; }

    /// <summary>
    /// Left Alt pressed on its own (true) or released (false). Alt is the PC-view modifier, and
    /// this runs inside the hook, before Windows decides which window gets the key, so whatever it
    /// does to keyboard focus decides where the Alt goes.
    /// </summary>
    public Action<bool>? PcAlt { get; set; }

    /// <summary>
    /// Whether a left Alt press belongs to the PC rather than the phone: pressed on its own, with no
    /// Ctrl (that is an app shortcut or AltGr), Shift or Windows key held. Right Alt is AltGr on many
    /// layouts and always goes to the phone.
    /// </summary>
    internal static bool IsPcAlt(int virtualKey, bool ctrl, bool shift, bool win) =>
        virtualKey == NativeMethods.VK_LMENU && !ctrl && !shift && !win;

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
        _taken.Clear();
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
        var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        if (nCode >= 0 && (isDown || message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP) && Record is { } record &&
            IsActive?.Invoke() == true && record((int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode, isDown))
        {
            return new IntPtr(1);
        }

        if (nCode >= 0 && message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            var released = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (released.vkCode == NativeMethods.VK_LMENU)
            {
                // Always reported, even when the window lost the foreground while Alt was held
                // (Alt+Tab), so the hold never outlives the key.
                PcAlt?.Invoke(false);
            }

            if (_taken.Release((int)released.vkCode)) return new IntPtr(1);
        }
        if (nCode >= 0 && message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var key = (int)data.vkCode;
            // A held key repeats its key-down; the repeats of a key the app took are the app's too.
            if (_taken.IsHeld(key)) return new IntPtr(1);
            var ctrl = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
            var shift = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);
            var win = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
            var mods = (ctrl ? KeyMods.Ctrl : 0) | (NativeMethods.IsKeyDown(NativeMethods.VK_MENU) ? KeyMods.Alt : 0) |
                (shift ? KeyMods.Shift : 0) | (win ? KeyMods.Win : 0);
            // Windows exposes AltGr as Ctrl+Right-Alt. It is text input, not one of the app's
            // Ctrl+Alt shortcuts; stealing it breaks characters on many keyboard layouts.
            var offered = CanOfferHotkey(NativeMethods.IsKeyDown(NativeMethods.VK_RMENU));
            if (offered && GlobalKey?.Invoke(key, mods) == true)
            {
                _taken.Take(key);
                return new IntPtr(1);
            }

            if (IsActive?.Invoke() == true)
            {
                if (IsPcAlt(key, ctrl, shift, win))
                {
                    PcAlt?.Invoke(true);
                }

                if (offered && KeyDown?.Invoke(key, mods) == true)
                {
                    _taken.Take(key);
                    return new IntPtr(1);
                }

                LastKeyToPhone = Environment.TickCount64;
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    internal static bool CanOfferHotkey(bool rightAltDown) => !rightAltDown;

    public void Dispose() => Uninstall();
}
