using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Delivers scrcpy keyboard shortcuts to the embedded window by posting key messages
/// directly to it. The launch contract pins scrcpy's modifier to Right Ctrl, so the
/// same sequence is always valid and nothing depends on which window is in the foreground.
/// </summary>
public static class ScrcpyShortcutSender
{
    private const uint MapvkVkToVsc = 0;

    public static bool Send(IntPtr hwnd, ScrcpyShortcut shortcut)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        var ok = true;
        ok &= Key(hwnd, NativeMethods.VK_RCONTROL, down: true, extended: true, alt: false);
        if (shortcut.Shift)
        {
            ok &= Key(hwnd, NativeMethods.VK_LSHIFT, down: true, extended: false, alt: false);
        }

        var extendedKey = shortcut.VirtualKey is NativeMethods.VK_LEFT or NativeMethods.VK_UP or NativeMethods.VK_RIGHT or NativeMethods.VK_DOWN;
        for (var i = 0; i < Math.Max(1, shortcut.Repeat); i++)
        {
            ok &= Key(hwnd, shortcut.VirtualKey, down: true, extended: extendedKey, alt: false);
            ok &= Key(hwnd, shortcut.VirtualKey, down: false, extended: extendedKey, alt: false);
        }

        if (shortcut.Shift)
        {
            ok &= Key(hwnd, NativeMethods.VK_LSHIFT, down: false, extended: false, alt: false);
        }

        ok &= Key(hwnd, NativeMethods.VK_RCONTROL, down: false, extended: true, alt: false);
        return ok;
    }

    private static bool Key(IntPtr hwnd, int virtualKey, bool down, bool extended, bool alt)
    {
        var scan = NativeMethods.MapVirtualKeyW((uint)virtualKey, MapvkVkToVsc) & 0xFF;
        long lParam = 1 | ((long)scan << 16);
        if (extended)
        {
            lParam |= 1L << 24;
        }

        if (alt)
        {
            lParam |= 1L << 29;
        }

        if (!down)
        {
            lParam |= (1L << 30) | (1L << 31);
        }

        var message = alt
            ? (down ? NativeMethods.WM_SYSKEYDOWN : NativeMethods.WM_SYSKEYUP)
            : (down ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP);
        return NativeMethods.PostMessage(hwnd, (uint)message, new IntPtr(virtualKey), new IntPtr(lParam));
    }
}
