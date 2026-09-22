using System.Runtime.InteropServices;

namespace Rex.Mirror.Native;

internal static class DpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public static bool IsPerMonitorV2(IntPtr hwnd)
    {
        var context = GetWindowDpiAwarenessContext(hwnd);
        return context != IntPtr.Zero && AreDpiAwarenessContextsEqual(context, PerMonitorAwareV2);
    }

    public static string Describe(IntPtr hwnd)
    {
        var awareness = IsPerMonitorV2(hwnd) ? "PerMonitorV2" : "not-PerMonitorV2";
        return $"{awareness}, {GetDpiForWindow(hwnd)} DPI";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
