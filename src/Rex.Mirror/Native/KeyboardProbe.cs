using System.Runtime.InteropServices;
using Rex.Core;

namespace Rex.Mirror.Native;

/// <summary>
/// What else a chord means on this PC, asked once when it is chosen: whether the keyboard layout types
/// a character with it (on many layouts left Ctrl+Alt is AltGr), and whether another app has claimed it
/// with <c>RegisterHotKey</c>. Neither stops the chord being saved; the person is told.
/// </summary>
internal static class KeyboardProbe
{
    private const int ProbeId = 0x5245;

    /// <summary>The character the current layout types with the chord, or null when it types none.</summary>
    public static string? CharacterFor(KeyChord chord)
    {
        var state = new byte[256];
        foreach (var (held, key) in new[] { (chord.Ctrl, NativeMethods.VK_CONTROL), (chord.Alt, NativeMethods.VK_MENU), (chord.Shift, NativeMethods.VK_SHIFT) })
        {
            if (held)
            {
                state[key] = 0x80;
            }
        }

        var buffer = new char[8];
        var layout = NativeMethods.GetKeyboardLayout(0);
        var scan = NativeMethods.MapVirtualKeyW((uint)chord.Key, NativeMethods.MAPVK_VK_TO_VSC);
        var count = NativeMethods.ToUnicodeEx((uint)chord.Key, scan, state, buffer, buffer.Length, NativeMethods.TOUNICODE_NO_STATE_CHANGE, layout);
        if (count <= 0)
        {
            return null;
        }

        var text = new string(buffer, 0, count);
        return text.All(char.IsControl) ? null : text;
    }

    /// <summary>True when another app has registered the chord as its own hot key.</summary>
    public static bool TakenElsewhere(KeyChord chord, IntPtr window)
    {
        var mods = NativeMethods.MOD_NOREPEAT |
            (chord.Ctrl ? NativeMethods.MOD_CONTROL : 0) | (chord.Alt ? NativeMethods.MOD_ALT : 0) |
            (chord.Shift ? NativeMethods.MOD_SHIFT : 0) | (chord.Win ? NativeMethods.MOD_WIN : 0);
        if (NativeMethods.RegisterHotKey(window, ProbeId, mods, (uint)chord.Key))
        {
            NativeMethods.UnregisterHotKey(window, ProbeId);
            return false;
        }

        return Marshal.GetLastWin32Error() == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED;
    }
}
