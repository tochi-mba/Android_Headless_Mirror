using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// The navigator's live picture of the whole phone, in a small window of its own just under the
/// overlay.
///
/// The picture changes every frame. Drawn in the overlay, a per-pixel-alpha window the size of the
/// whole mirror, each change made Windows read the entire overlay back from the GPU and push it
/// through the CPU again, megabytes a frame for a picture a hundred pixels wide. This window is an
/// ordinary one the size of the picture: WPF draws it on the GPU and only its own few pixels change.
/// The picture fills it completely, so it needs no per-pixel transparency; the opacity setting is a
/// single alpha for the whole window, which the compositor applies for free. The frame, the
/// viewport box and the drag handles stay in the overlay above it, which still takes the mouse.
/// </summary>
public sealed class NavigatorPictureWindow : Window
{
    private readonly Window _owner;
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private WriteableBitmap? _bitmap;
    private HwndSource? _source;
    private RECT _placed;
    private byte _alpha = 255;

    public NavigatorPictureWindow(Window owner)
    {
        _owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = false;
        Width = 1;
        Height = 1;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x11));
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.Linear);
        Content = _image;

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            // Layered with one alpha for the whole window, and transparent to the mouse: every click
            // on the navigator belongs to the overlay's frame above it, never to this picture.
            var exStyle = NativeMethods.GetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            NativeMethods.SetLayeredWindowAttributes(_source.Handle, 0, _alpha, NativeMethods.LWA_ALPHA);
        };
    }

    public bool HasPicture => IsVisible && _image.Source is not null;

    /// <summary>Shows one sharp frame of the whole phone screen.</summary>
    public void SetFrame(AmbientFrame? frame)
    {
        if (frame is null)
        {
            _image.Source = null;
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32, null);
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        _image.Source = _bitmap;
    }

    /// <summary>The whole-window alpha for an opacity between 0 and 1.</summary>
    internal static byte AlphaFor(double opacity) => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);

    /// <summary>
    /// Puts the picture at a screen rectangle (physical pixels), just below <paramref name="above"/>
    /// in the z-order, or hides it when <paramref name="show"/> is false.
    /// </summary>
    public void Place(bool show, RECT screenPixels, double opacity, IntPtr above)
    {
        if (!show || screenPixels.Width <= 0 || screenPixels.Height <= 0 || above == IntPtr.Zero)
        {
            _placed = default;
            if (IsVisible)
            {
                Hide();
            }

            _image.Source = null;
            return;
        }

        if (!IsVisible)
        {
            Owner ??= _owner;
            Show();
            _placed = default;
        }

        var alpha = AlphaFor(opacity);
        if (_source is not null && alpha != _alpha)
        {
            _alpha = alpha;
            NativeMethods.SetLayeredWindowAttributes(_source.Handle, 0, alpha, NativeMethods.LWA_ALPHA);
        }

        if (_source is null || Same(screenPixels, _placed))
        {
            return;
        }

        // Inserting after the overlay puts this window directly beneath it: above the mirror,
        // under the frame and the handles that take the mouse.
        NativeMethods.SetWindowPos(_source.Handle, above, screenPixels.Left, screenPixels.Top, screenPixels.Width, screenPixels.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
        _placed = screenPixels;
    }

    private static bool Same(RECT a, RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
}
