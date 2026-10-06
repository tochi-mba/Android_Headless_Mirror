using System.Runtime.InteropServices;

namespace Rex.Tests.Support;

/// <summary>
/// The website's camera: the window exactly as it is seen, without the invisible borders Windows
/// keeps around it for resizing (which would take in whatever is behind), and the part of the
/// monitor the taskbar leaves free, so a picture never has the taskbar over the window.
/// </summary>
public sealed partial class AppProcess
{
    private const int DwmwaExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    /// <summary>The monitor's work area (the part the taskbar leaves), in physical pixels.</summary>
    public System.Drawing.Rectangle WorkArea()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { return System.Windows.Forms.Screen.FromHandle(FindMainWindow()).WorkingArea; }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    /// <summary>The window as it is seen: its frame without the resizing borders.</summary>
    public async Task<System.Drawing.Bitmap> CaptureFrameAsync()
    {
        await Task.Yield();
        _ = DwmFlush();
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var hwnd = FindMainWindow();
            Assert.Equal(0, DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var frame, Marshal.SizeOf<RECT>()));
            var bitmap = new System.Drawing.Bitmap(frame.Right - frame.Left, frame.Bottom - frame.Top);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(frame.Left, frame.Top, 0, 0, bitmap.Size);
            }

            return bitmap;
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }
}
