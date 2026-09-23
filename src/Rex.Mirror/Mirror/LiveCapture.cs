using Rex.Core;
using Rex.Mirror.Native;
using Drawing = System.Drawing;

namespace Rex.Mirror.Mirror;

/// <summary>One captured frame: a small BGRA copy of the mirror surface, already blurred.</summary>
public sealed record AmbientFrame(byte[] Pixels, int Width, int Height);

/// <summary>
/// Takes the soft background straight off the screen, small and already blurred.
///
/// The blur is the expensive part, and it happens on a copy a couple of hundred pixels wide rather
/// than at display size, so raising the frame rate costs almost nothing and the UI thread only ever
/// copies finished pixels.
/// </summary>
public sealed class LiveCapture : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Drawing.Bitmap? _small;

    // Two picture buffers, used in turn: the window is still reading the frame just handed over
    // while the next one is being taken, and one buffer would be rewritten underneath it.
    private readonly byte[][] _buffers = [[], []];
    private byte[] _scratch = [];
    private Drawing.Bitmap? _full;
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
            _small = new Drawing.Bitmap(captureWidth, captureHeight, Drawing.Imaging.PixelFormat.Format32bppRgb);
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
            Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            for (var y = 0; y < captureHeight; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    bits.Scan0 + y * bits.Stride, pixels, y * captureWidth * 4, captureWidth * 4);
            }

            // The screen has no transparency to copy, so the fourth byte of each pixel is not a
            // meaningful alpha. The window draws the picture at its own opacity, so every pixel is
            // made opaque here; left as it comes, the whole thing is invisible.
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
    /// Copies the screen rectangle, then shrinks the copy.
    ///
    /// Stretching straight from the screen in one call is cheaper, and was tried: some machines
    /// hand back a black rectangle for it, and a soft background of pure black is the same as none
    /// at all. This way is a few megabytes of blit per frame more, at a frame rate measured in the
    /// teens, and it is the same picture on every machine.
    /// </summary>
    private bool CopyScreen(Drawing.Bitmap target, RECT screenRect, int width, int height)
    {
        if (_full is null || _full.Width != screenRect.Width || _full.Height != screenRect.Height)
        {
            _full?.Dispose();
            _full = new Drawing.Bitmap(screenRect.Width, screenRect.Height, Drawing.Imaging.PixelFormat.Format32bppRgb);
        }

        try
        {
            using (var graphics = Drawing.Graphics.FromImage(_full))
            {
                graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, _full.Size);
            }

            using (var graphics = Drawing.Graphics.FromImage(target))
            {
                graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.Half;
                graphics.DrawImage(_full, new Drawing.Rectangle(0, 0, width, height));
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            // The desktop refuses a copy while it is locked or switching sessions; skip the frame.
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _full?.Dispose();
        _small?.Dispose();
        _oneAtATime.Dispose();
    }
}
