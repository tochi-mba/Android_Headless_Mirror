using Rex.Core;
using Rex.Mirror.Native;
using Drawing = System.Drawing;

namespace Rex.Mirror.Mirror;

/// <summary>One captured picture: a small BGRA copy of the mirror surface.</summary>
public sealed record AmbientFrame(byte[] Pixels, int Width, int Height);

/// <summary>What one capture produced: the blurred copy for the soft background and the sharp one for the navigator, either may be missing.</summary>
public sealed record CaptureResult(AmbientFrame? Ambient, AmbientFrame? Preview);

/// <summary>
/// Takes the soft background and the navigator picture straight off the screen, small and, for
/// the background, already blurred.
///
/// The picture comes through the GPU (<see cref="GpuCapture"/>) whenever the machine allows it:
/// the compositor's frame is cut and shrunk on the graphics card and only the small result is
/// read back, and nothing is read at all while the desktop has not changed. Where that path is
/// refused the old GDI copy is used instead. Either way the blur happens on a copy a couple of
/// hundred pixels wide, and the UI thread only ever copies finished pixels.
/// </summary>
public sealed class LiveCapture : IDisposable
{
    /// <summary>How long closing waits for a capture in flight before leaving its graphics objects to Windows.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

    private readonly WorkGate _oneAtATime = new();
    private readonly GpuCapture _gpu = new();
    private readonly Action<string>? _log;
    private string? _gpuFailureLogged;
    private string _path = "gpu";

    // Two buffers per picture, used in turn: the window is still reading the frame just handed
    // over while the next one is being taken, and one buffer would be rewritten underneath it.
    private readonly byte[]?[] _ambientBuffers = new byte[2][];
    private readonly byte[]?[] _previewBuffers = new byte[2][];
    private byte[] _scratch = [];
    private int _next;

    private Drawing.Bitmap? _full;
    private Drawing.Bitmap? _smallAmbient;
    private Drawing.Bitmap? _smallPreview;
    private volatile bool _disposed;

    public LiveCapture(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>"gpu" while desktop duplication is doing the work, "gdi" when the last picture was copied the slow way.</summary>
    public string Path => _path;

    /// <summary>
    /// A fresh capture of <paramref name="screenRect"/> (physical pixels): the blurred background
    /// when <paramref name="ambient"/> is set, the sharp preview when <paramref name="previewWidth"/>
    /// is positive. Null when nothing has changed on screen, the frame cannot be taken, or another
    /// capture is still in flight.
    /// </summary>
    public async Task<CaptureResult?> CaptureAsync(RECT screenRect, double blurRadius, bool ambient, int previewWidth)
    {
        if (_disposed || screenRect.Width < 8 || screenRect.Height < 8 || (!ambient && previewWidth <= 0) ||
            !_oneAtATime.TryEnter())
        {
            return null;
        }

        // The gate is left on the worker itself: Dispose waits on the UI thread for it, so leaving
        // it from a continuation queued to that same thread would never happen while it waits.
        return await Task.Run(() =>
        {
            try
            {
                return _disposed ? null : Capture(screenRect, blurRadius, ambient, previewWidth);
            }
            finally
            {
                _oneAtATime.Exit();
            }
        }).ConfigureAwait(true);
    }

    private CaptureResult? Capture(RECT screenRect, double blurRadius, bool ambient, int previewWidth)
    {
        var ambientWidth = ambient ? Math.Min(AmbientBlur.CaptureWidth(blurRadius), screenRect.Width) : 0;
        previewWidth = Math.Min(previewWidth, screenRect.Width);
        var slot = _next;
        _next = 1 - _next;

        AmbientFrame? ambientFrame = null;
        AmbientFrame? previewFrame = null;
        if (_gpu.IsAvailable)
        {
            try
            {
                var pictures = _gpu.Capture(screenRect, [ambientWidth, previewWidth], [_ambientBuffers[slot], _previewBuffers[slot]]);
                _path = "gpu";
                if (pictures is null)
                {
                    return null;
                }

                if (pictures[0] is { } small)
                {
                    _ambientBuffers[slot] = small.Pixels;
                    ambientFrame = Blur(small.Pixels, small.Width, small.Height, blurRadius, screenRect.Width);
                }

                if (pictures[1] is { } preview)
                {
                    _previewBuffers[slot] = preview.Pixels;
                    previewFrame = new AmbientFrame(preview.Pixels, preview.Width, preview.Height);
                }

                return new CaptureResult(ambientFrame, previewFrame);
            }
            catch (GpuCaptureException ex)
            {
                // Said once per reason: a machine that refuses every time would otherwise fill the log.
                if (_gpuFailureLogged != ex.Message)
                {
                    _gpuFailureLogged = ex.Message;
                    _log?.Invoke("GPU capture is not available right now (" + ex.Message + "); copying the screen instead and trying the GPU again shortly.");
                }
            }
        }

        _path = "gdi";

        if (!CopyScreen(screenRect))
        {
            return null;
        }

        if (ambientWidth > 0)
        {
            var height = ScaledHeight(screenRect, ambientWidth);
            _smallAmbient = Shrink(_smallAmbient, ambientWidth, height);
            var pixels = ReadPixels(_smallAmbient, _ambientBuffers[slot]);
            _ambientBuffers[slot] = pixels;
            ambientFrame = Blur(pixels, ambientWidth, height, blurRadius, screenRect.Width);
        }

        if (previewWidth > 0)
        {
            var height = ScaledHeight(screenRect, previewWidth);
            _smallPreview = Shrink(_smallPreview, previewWidth, height);
            var pixels = ReadPixels(_smallPreview, _previewBuffers[slot]);
            _previewBuffers[slot] = pixels;
            previewFrame = new AmbientFrame(pixels, previewWidth, height);
        }

        return new CaptureResult(ambientFrame, previewFrame);
    }

    private AmbientFrame Blur(byte[] pixels, int width, int height, double blurRadius, int surfaceWidth)
    {
        if (_scratch.Length < pixels.Length)
        {
            _scratch = new byte[pixels.Length];
        }

        AmbientBlur.Apply(pixels, width, height, AmbientBlur.SmallRadius(blurRadius, width, surfaceWidth), scratch: _scratch);
        return new AmbientFrame(pixels, width, height);
    }

    private static int ScaledHeight(RECT screenRect, int width) =>
        Math.Max(1, (int)Math.Round((double)width * screenRect.Height / screenRect.Width));

    /// <summary>
    /// Copies the screen rectangle the GDI way, then shrinks the copy.
    ///
    /// Stretching straight from the screen in one call is cheaper, and was tried: some machines
    /// hand back a black rectangle for it, and a soft background of pure black is the same as none
    /// at all. This path only runs where the GPU refuses, so its cost is accepted for the same picture
    /// on every machine.
    /// </summary>
    private bool CopyScreen(RECT screenRect)
    {
        if (_full is null || _full.Width != screenRect.Width || _full.Height != screenRect.Height)
        {
            _full?.Dispose();
            _full = new Drawing.Bitmap(screenRect.Width, screenRect.Height, Drawing.Imaging.PixelFormat.Format32bppRgb);
        }

        try
        {
            using var graphics = Drawing.Graphics.FromImage(_full);
            graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, _full.Size);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            // The desktop refuses a copy while it is locked or switching sessions; skip the frame.
            return false;
        }
    }

    private Drawing.Bitmap Shrink(Drawing.Bitmap? target, int width, int height)
    {
        if (target is null || target.Width != width || target.Height != height)
        {
            target?.Dispose();
            target = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppRgb);
        }

        using var graphics = Drawing.Graphics.FromImage(target);
        graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(_full!, new Drawing.Rectangle(0, 0, width, height));
        return target;
    }

    private static byte[] ReadPixels(Drawing.Bitmap bitmap, byte[]? reuse)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = reuse is { } buffer && buffer.Length == width * height * 4 ? buffer : new byte[width * height * 4];
        var bits = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bits.Scan0 + y * bits.Stride, pixels, y * width * 4, width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        // The screen has no transparency to copy, so the fourth byte of each pixel is not a
        // meaningful alpha. The window draws the picture at its own opacity, so every pixel is
        // made opaque here; left as it comes, the whole thing is invisible.
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return pixels;
    }

    /// <summary>
    /// Releases the graphics objects once no capture is using them. A capture still reading back
    /// is waited for; releasing the device under it crashed the app on quit.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (!_oneAtATime.Close(CloseWait))
        {
            _log?.Invoke("A capture was still running as the window closed; its graphics objects are left for Windows to release.");
            return;
        }

        _gpu.Dispose();
        _full?.Dispose();
        _smallAmbient?.Dispose();
        _smallPreview?.Dispose();
    }
}
