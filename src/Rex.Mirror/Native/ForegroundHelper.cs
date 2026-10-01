namespace Rex.Mirror.Native;

/// <summary>
/// Brings a window to the front from a key the keyboard hook saw. Windows lets a process take the
/// foreground only in some circumstances, and a key that never reached this process's own queue is
/// not always one of them, so each step is tried only when the one before did not take.
/// </summary>
internal static class ForegroundHelper
{
    /// <summary>Which step put the window in front: "set", "attached", "switched", or "refused".</summary>
    public static string Bring(IntPtr hwnd)
    {
        if (TryStep(hwnd, () => NativeMethods.SetForegroundWindow(hwnd)))
        {
            return "set";
        }

        // Sharing the foreground thread's input state for the call is what Windows accepts from a
        // process that has just seen the user's key.
        var foreground = NativeMethods.GetForegroundWindow();
        var theirs = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var ours = NativeMethods.GetCurrentThreadId();
        if (theirs != 0 && theirs != ours && NativeMethods.AttachThreadInput(ours, theirs, true))
        {
            try
            {
                if (TryStep(hwnd, () => NativeMethods.BringWindowToTop(hwnd) & NativeMethods.SetForegroundWindow(hwnd)))
                {
                    return "attached";
                }
            }
            finally
            {
                NativeMethods.AttachThreadInput(ours, theirs, false);
            }
        }

        NativeMethods.SwitchToThisWindow(hwnd, true);
        return InFront(hwnd) ? "switched" : "refused";
    }

    /// <summary>Whether the window, or a window inside it, is the foreground window.</summary>
    public static bool InFront(IntPtr hwnd) => NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) == hwnd;

    private static bool TryStep(IntPtr hwnd, Func<bool> step)
    {
        step();
        return InFront(hwnd);
    }
}
