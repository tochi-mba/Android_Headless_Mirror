using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rex.Mirror.Native;
using Drawing = System.Drawing;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Grabs the mirror surface straight off the screen (the pixels scrcpy already drew) and
/// downsizes it for the soft background. No phone round trip, so it follows the video at the
/// configured frame rate for the cost of one small screen copy per frame.
/// </summary>
public sealed class LiveCapture : IDisposable
{
    private const int TargetWidth = 192;

    private Drawing.Bitmap? _full;
    private Drawing.Bitmap? _small;
    private WriteableBitmap? _frame;

    /// <summary>Returns a fresh frame of <paramref name="screenRect"/> (physical pixels), or null when it cannot be captured.</summary>
    public BitmapSource? Capture(RECT screenRect)
    {
        if (screenRect.Width < 8 || screenRect.Height < 8)
        {
            return null;
        }

        if (_full is null || _full.Width != screenRect.Width || _full.Height != screenRect.Height)
        {
            _full?.Dispose();
            _full = new Drawing.Bitmap(screenRect.Width, screenRect.Height, Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        var smallHeight = Math.Max(1, (int)Math.Round((double)TargetWidth * screenRect.Height / screenRect.Width));
        if (_small is null || _small.Height != smallHeight)
        {
            _small?.Dispose();
            _small = new Drawing.Bitmap(TargetWidth, smallHeight, Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _frame = null;
        }

        try
        {
            using (var graphics = Drawing.Graphics.FromImage(_full))
            {
                graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, _full.Size);
            }

            using (var graphics = Drawing.Graphics.FromImage(_small))
            {
                graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.DrawImage(_full, new Drawing.Rectangle(0, 0, _small.Width, _small.Height));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            // The desktop can refuse a copy while it is locked or switching sessions; skip the frame.
            return null;
        }

        var bits = _small.LockBits(new Drawing.Rectangle(0, 0, _small.Width, _small.Height), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            _frame ??= new WriteableBitmap(_small.Width, _small.Height, 96, 96, PixelFormats.Pbgra32, null);
            _frame.WritePixels(new System.Windows.Int32Rect(0, 0, _small.Width, _small.Height), bits.Scan0, bits.Stride * _small.Height, bits.Stride);
        }
        finally
        {
            _small.UnlockBits(bits);
        }

        return _frame;
    }

    public void Dispose()
    {
        _full?.Dispose();
        _small?.Dispose();
    }
}
