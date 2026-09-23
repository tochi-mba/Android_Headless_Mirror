using Rex.Core;
using Rex.Mirror.Native;
using Drawing = System.Drawing;

namespace Rex.Mirror.Mirror;

/// <summary>One captured frame: a small BGRA copy of the mirror surface, already blurred.</summary>
public sealed record AmbientFrame(byte[] Pixels, int Width, int Height);

/// <summary>
/// Takes the soft background straight off the screen, small and already blurred.
///
/// The work is deliberately tiny: one stretched copy straight into a bitmap a couple of hundred
/// pixels wide, and a box blur over that. Nothing scales with the window size, so raising the
/// frame rate costs almost nothing, and the UI thread only ever copies finished pixels.
/// </summary>
public sealed class LiveCapture : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Drawing.Bitmap? _small;

    // Two picture buffers, used in turn: the window is still reading the frame just handed over
    // while the next one is being taken, and one buffer would be rewritten underneath it.
    private readonly byte[][] _buffers = [[], []];
    private byte[] _scratch = [];
    private int _next;
    private bool _disposed;

    /// <summary>
    /// A fresh frame of <paramref name="screenRect"/> (physical pixels), or null when the frame
    /// cannot be taken or another capture is still in flight.
    /// </summary>
    public async Task<AmbientFrame?> CaptureAsync(RECT screenRect, double blurRadius)
    {
        if (_disposed || screenRect.Width < 8 || screenRect.Height < 8 || !await _oneAtATime.WaitAsync(0).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await Task.Run(() => Capture(screenRect, blurRadius)).ConfigureAwait(true);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private AmbientFrame? Capture(RECT screenRect, double blurRadius)
    {
        var captureWidth = Math.Min(AmbientBlur.CaptureWidth(blurRadius), screenRect.Width);
        var captureHeight = Math.Max(1, (int)Math.Round((double)captureWidth * screenRect.Height / screenRect.Width));

        if (_small is null || _small.Width != captureWidth || _small.Height != captureHeight)
        {
            _small?.Dispose();
            _small = new Drawing.Bitmap(captureWidth, captureHeight, Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _buffers[0] = new byte[captureWidth * captureHeight * 4];
            _buffers[1] = new byte[captureWidth * captureHeight * 4];
            _scratch = new byte[captureWidth * captureHeight * 4];
        }

        var pixels = _buffers[_next];
        _next = 1 - _next;

        if (!CopyScreen(_small, screenRect, captureWidth, captureHeight))
        {
            return null;
        }

        var bits = _small.LockBits(
            new Drawing.Rectangle(0, 0, captureWidth, captureHeight),
            Drawing.Imaging.ImageLockMode.ReadOnly,
            Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < captureHeight; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    bits.Scan0 + y * bits.Stride, pixels, y * captureWidth * 4, captureWidth * 4);
            }

            // A copy from the screen carries colour but no transparency, so every pixel arrives
            // fully transparent. The background is drawn at its own opacity, and an image whose
            // pixels are all see-through would simply never appear.
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }
        finally
        {
            _small.UnlockBits(bits);
        }

        AmbientBlur.Apply(
            pixels,
            captureWidth,
            captureHeight,
            AmbientBlur.SmallRadius(blurRadius, captureWidth, screenRect.Width),
            scratch: _scratch);
        return new AmbientFrame(pixels, captureWidth, captureHeight);
    }

    /// <summary>
    /// Copies the screen rectangle straight into the small bitmap, shrinking it on the way.
    ///
    /// Copying the rectangle at full size and shrinking it afterwards makes the work grow with the
    /// window: on a large display that is tens of megabytes moved per frame. One stretched copy
    /// does both steps at the size of the result, which is a few hundred pixels wide whatever the
    /// window is doing, so the frame rate can go up without the cost following it.
    /// </summary>
    private static bool CopyScreen(Drawing.Bitmap target, RECT screenRect, int width, int height)
    {
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        Drawing.Graphics? graphics = null;
        var destination = IntPtr.Zero;
        try
        {
            graphics = Drawing.Graphics.FromImage(target);
            destination = graphics.GetHdc();
            NativeMethods.SetStretchBltMode(destination, NativeMethods.HALFTONE);
            NativeMethods.SetBrushOrgEx(destination, 0, 0, IntPtr.Zero);
            return NativeMethods.StretchBlt(
                destination, 0, 0, width, height,
                screen, screenRect.Left, screenRect.Top, screenRect.Width, screenRect.Height,
                NativeMethods.SRCCOPY);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            // The desktop refuses a copy while it is locked or switching sessions; skip the frame.
            return false;
        }
        finally
        {
            if (destination != IntPtr.Zero)
            {
                graphics!.ReleaseHdc(destination);
            }

            graphics?.Dispose();
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _small?.Dispose();
        _oneAtATime.Dispose();
    }
}
