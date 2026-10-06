using SharpGen.Runtime;
using Rex.Mirror.Native;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Copies a rectangle of the desktop through the GPU instead of GDI.
///
/// A GDI copy of the screen (BitBlt, CopyFromScreen, PrintWindow) makes the compositor stop and
/// read the desktop back through system memory: 30 ms for a phone-sized window and over 100 ms
/// for a full display, every frame, whether or not anything changed. DXGI desktop duplication
/// hands over the compositor's own frame as a texture only when the desktop has changed, the
/// region is cut out and shrunk on the GPU with a mip chain, and the only thing read back is the
/// small picture the caller asked for. That is well under a millisecond, and nothing at all while
/// the phone screen sits still.
///
/// It is not available everywhere: remote desktop sessions, some virtual displays and a locked
/// desktop refuse it. The caller keeps the GDI copy for those.
/// </summary>
public sealed class GpuCapture : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private RECT _outputBounds;
    private ID3D11Texture2D? _region;
    private ID3D11ShaderResourceView? _regionView;
    private int _regionWidth;
    private int _regionHeight;
    private int _regionLevels;
    private readonly Dictionary<int, ID3D11Texture2D> _stagingByLevel = [];
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromSeconds(60);
    private DateTime _retryAt = DateTime.MinValue;
    private TimeSpan _backoff = FirstRetry;
    private bool _disposed;

    /// <summary>One shrunk copy of the region, BGRA with opaque alpha.</summary>
    public sealed record Picture(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// Whether the device runs on Microsoft's own software adapter (the Basic Render Driver), as
    /// it does with no graphics card, over Remote Desktop and in many virtual machines: then the
    /// "GPU" work here is the processor's.
    /// </summary>
    public bool OnSoftwareAdapter { get; private set; }

    /// <summary>The vendor id Windows gives its own adapters, the software one among them.</summary>
    private const uint MicrosoftVendorId = 0x1414;

    /// <summary>Why the GPU path last failed, for the log; null while it works.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>
    /// False while the path is resting after a failure. Most failures pass on their own (the
    /// desktop was locked, a display was being reconfigured, a driver restarted), so it is tried
    /// again after a pause that grows each time, rather than written off for the session.
    /// </summary>
    public bool IsAvailable => !_disposed && DateTime.UtcNow >= _retryAt;

    /// <summary>
    /// The mip level whose width is still at least <paramref name="targetWidth"/>, so the picture
    /// handed back is never smaller than asked for, and its size at that level.
    /// </summary>
    internal static (int Level, int Width, int Height) LevelFor(int regionWidth, int regionHeight, int targetWidth)
    {
        var level = 0;
        while (targetWidth > 0 && (regionWidth >> (level + 1)) >= targetWidth && (regionHeight >> (level + 1)) >= 1)
        {
            level++;
        }

        return (level, Math.Max(1, regionWidth >> level), Math.Max(1, regionHeight >> level));
    }

    /// <summary>
    /// Captures the region at each requested width. Returns null when the desktop has not changed
    /// since the last call (nothing to draw), and throws <see cref="GpuCaptureException"/> when the
    /// GPU path stops working so the caller can fall back.
    /// </summary>
    public Picture?[]? Capture(RECT screenRect, ReadOnlySpan<int> targetWidths, byte[]?[] reuse)
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            EnsureDevice();
            if (!EnsureDuplication(screenRect))
            {
                // The mirror is on no display right now (minimized, or mid-move): nothing to copy.
                return null;
            }

            var rect = Clamp(screenRect, _outputBounds);
            if (rect.Width < 8 || rect.Height < 8)
            {
                return null;
            }

            var acquired = _duplication!.AcquireNextFrame(0, out var info, out var resource);
            if (acquired == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                return null;
            }

            acquired.CheckError();
            try
            {
                if (info.LastPresentTime == 0)
                {
                    // Only the pointer moved; the desktop image is what it was.
                    return null;
                }

                using var desktop = resource.QueryInterface<ID3D11Texture2D>();
                EnsureRegion(rect.Width, rect.Height);
                _context!.CopySubresourceRegion(_region!, 0, 0, 0, 0, desktop, 0,
                    new Box(rect.Left - _outputBounds.Left, rect.Top - _outputBounds.Top, 0, rect.Right - _outputBounds.Left, rect.Bottom - _outputBounds.Top, 1));
                _context.GenerateMips(_regionView!);

                var pictures = new Picture?[targetWidths.Length];
                for (var i = 0; i < targetWidths.Length; i++)
                {
                    if (targetWidths[i] <= 0)
                    {
                        continue;
                    }

                    var (level, width, height) = LevelFor(rect.Width, rect.Height, targetWidths[i]);
                    pictures[i] = ReadBack(level, width, height, reuse[i]);
                }

                _backoff = FirstRetry;
                UnavailableReason = null;
                return pictures;
            }
            finally
            {
                resource.Dispose();
                _duplication.ReleaseFrame();
            }
        }
        catch (SharpGenException ex)
        {
            Reset();
            if (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
            {
                // The desktop switched (resolution, sleep, UAC); the next call starts over.
                return null;
            }

            _retryAt = DateTime.UtcNow + _backoff;
            _backoff = _backoff + _backoff > LongestRetry ? LongestRetry : _backoff + _backoff;
            UnavailableReason = ex.Message;
            throw new GpuCaptureException(ex.Message, ex);
        }
    }

    private Picture ReadBack(int level, int width, int height, byte[]? reuse)
    {
        if (!_stagingByLevel.TryGetValue(level, out var staging) || staging.Description.Width != width || staging.Description.Height != height)
        {
            staging?.Dispose();
            staging = _device!.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, (uint)width, (uint)height, arraySize: 1, mipLevels: 1,
                bindFlags: BindFlags.None, usage: ResourceUsage.Staging, cpuAccessFlags: CpuAccessFlags.Read));
            _stagingByLevel[level] = staging;
        }

        var context = _context!;
        context.CopySubresourceRegion(staging, 0, 0, 0, 0, _region!, (uint)level, null);
        var pixels = reuse is { } buffer && buffer.Length == width * height * 4 ? buffer : new byte[width * height * 4];
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(mapped.DataPointer + (nint)(y * (long)mapped.RowPitch), pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        // The desktop carries no alpha worth keeping; the window draws at its own opacity.
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return new Picture(pixels, width, height);
    }

    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out ID3D11Device? device, out ID3D11DeviceContext? context);
        result.CheckError();
        _device = device;
        _context = context;
        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        OnSoftwareAdapter = adapter.Description.VendorId == MicrosoftVendorId;
    }

    private bool EnsureDuplication(RECT screenRect)
    {
        if (_duplication is not null && Contains(_outputBounds, screenRect.Left + screenRect.Width / 2, screenRect.Top + screenRect.Height / 2))
        {
            return true;
        }

        _duplication?.Dispose();
        _duplication = null;

        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        var centreX = screenRect.Left + screenRect.Width / 2;
        var centreY = screenRect.Top + screenRect.Height / 2;
        for (uint i = 0; adapter.EnumOutputs(i, out var output).Success; i++)
        {
            using (output)
            {
                var description = output.Description;
                var bounds = new RECT
                {
                    Left = description.DesktopCoordinates.Left,
                    Top = description.DesktopCoordinates.Top,
                    Right = description.DesktopCoordinates.Right,
                    Bottom = description.DesktopCoordinates.Bottom,
                };
                if (!description.AttachedToDesktop || !Contains(bounds, centreX, centreY))
                {
                    continue;
                }

                if (description.Rotation != ModeRotation.Identity)
                {
                    throw new SharpGenException("The display is rotated, which desktop duplication does not hand back the right way up.");
                }

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device);
                _outputBounds = bounds;
                DisposeRegion();
                return true;
            }
        }

        return false;
    }

    private void EnsureRegion(int width, int height)
    {
        if (_region is not null && _regionWidth == width && _regionHeight == height)
        {
            return;
        }

        DisposeRegion();
        _regionWidth = width;
        _regionHeight = height;
        _regionLevels = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));
        _region = _device!.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, arraySize: 1, mipLevels: (uint)_regionLevels,
            bindFlags: BindFlags.ShaderResource | BindFlags.RenderTarget, usage: ResourceUsage.Default,
            cpuAccessFlags: CpuAccessFlags.None, miscFlags: ResourceOptionFlags.GenerateMips));
        _regionView = _device.CreateShaderResourceView(_region);
    }

    private void DisposeRegion()
    {
        _regionView?.Dispose();
        _regionView = null;
        _region?.Dispose();
        _region = null;
        foreach (var staging in _stagingByLevel.Values)
        {
            staging.Dispose();
        }

        _stagingByLevel.Clear();
    }

    private void Reset()
    {
        DisposeRegion();
        _duplication?.Dispose();
        _duplication = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }

    private static bool Contains(RECT bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;

    private static RECT Clamp(RECT rect, RECT bounds) => new()
    {
        Left = Math.Max(rect.Left, bounds.Left),
        Top = Math.Max(rect.Top, bounds.Top),
        Right = Math.Min(rect.Right, bounds.Right),
        Bottom = Math.Min(rect.Bottom, bounds.Bottom),
    };

    public void Dispose()
    {
        _disposed = true;
        Reset();
    }
}

/// <summary>The GPU path stopped working for this session; the message says why.</summary>
public sealed class GpuCaptureException(string message, Exception inner) : Exception(message, inner);
