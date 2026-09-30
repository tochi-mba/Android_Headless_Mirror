using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Rex.Core;

namespace Rex.Mirror.Mirror;

/// <summary>
/// The soft background, drawn by the main window itself behind the mirror.
///
/// It used to live in the transparent overlay that floats above the phone. That overlay is a
/// per-pixel-alpha window, and Windows redraws such a window on the CPU and copies all of it on
/// every change, so a background covering the whole mirror area cost a third of a core at 15
/// frames a second and most of one at 60. Here it is ordinary WPF content, drawn on the GPU with
/// the rest of the window, and the viewport window is clipped to the phone picture
/// (<see cref="MirrorHost"/>) so the margins show it. The viewport and this view are the same ink
/// colour, so nothing looks different except that it no longer costs anything to watch.
/// </summary>
public sealed class AmbientView : Canvas
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _container = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _tint = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private WriteableBitmap? _bitmap;
    private LayoutKey? _key;

    /// <summary>
    /// Everything the layout depends on. Rebuilding the same geometry every tick costs a redraw
    /// for a picture that has not moved; comparing this first means the work happens only when
    /// something changed.
    /// </summary>
    private readonly record struct LayoutKey(
        bool Enabled, RectD Surface, double Width, double Height, double Scale, double SourceWidth, double SourceHeight,
        double Opacity, string Placement, string Scaling, double Size, double OffsetX, double OffsetY,
        bool FlipHorizontal, double EdgeFade, double TintStrength, double TintHue);

    public AmbientView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        // The frame is a couple of hundred pixels wide and already blurred; a cheap linear upscale
        // is exactly right and there is no detail for a better filter to keep.
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.LowQuality);
        _container.Children.Add(_image);
        _container.Children.Add(_tint);
        Children.Add(_container);
    }

    public bool FrameAvailable => _image.Source is not null;

    public bool IsShowing => IsVisible && _container.Visibility == Visibility.Visible && _image.Source is not null;

    /// <summary>Shows one captured frame. The pixels are already blurred, so the picture is simply scaled up.</summary>
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

        // Switching the background off clears the picture but keeps the bitmap, so this has to be
        // set on the way back in and not only when a new bitmap is made.
        _image.Source = _bitmap;
    }

    /// <summary>
    /// Lays the background out exactly as configured: the capture is scaled (cover, fit or stretch,
    /// times Size), shifted by the offsets, clipped to the chosen margins, and the phone picture
    /// itself is always cut out so the live video is never covered. <paramref name="surfacePixels"/>
    /// is the picture in the viewport's physical pixels.
    /// </summary>
    public void Update(bool enabled, RectD surfacePixels, AmbientSettings settings)
    {
        var width = Finite(ActualWidth);
        var height = Finite(ActualHeight);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var source = _image.Source;
        var key = new LayoutKey(
            enabled, surfacePixels, width, height, scale, source?.Width ?? 0, source?.Height ?? 0,
            settings.Opacity, settings.Placement, settings.Scaling, settings.Size, settings.OffsetX,
            settings.OffsetY, settings.FlipHorizontal, settings.EdgeFade, settings.TintStrength, settings.TintHue);
        if (key == _key)
        {
            return;
        }

        _key = key;
        _container.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            return;
        }

        _image.Opacity = settings.Opacity;
        _container.Width = width;
        _container.Height = height;

        if (source is { Width: > 0, Height: > 0 } && width > 0 && height > 0)
        {
            var (imageWidth, imageHeight) = AmbientLayout.ImageSize(settings, source.Width, source.Height, width, height);
            _image.Width = imageWidth;
            _image.Height = imageHeight;
            SetLeft(_image, (width - imageWidth) / 2 + settings.OffsetX * width / 2);
            SetTop(_image, (height - imageHeight) / 2 + settings.OffsetY * height / 2);
            _image.RenderTransformOrigin = new Point(0.5, 0.5);
            _image.RenderTransform = Frozen(new ScaleTransform(settings.FlipHorizontal ? -1 : 1, 1));
        }

        _tint.Width = width;
        _tint.Height = height;
        _tint.Visibility = settings.TintStrength > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (settings.TintStrength > 0)
        {
            var (r, g, b) = AmbientLayout.HueToRgb(settings.TintHue);
            _tint.Fill = Frozen(new SolidColorBrush(Color.FromRgb(r, g, b)));
            _tint.Opacity = settings.TintStrength * settings.Opacity;
        }

        _container.OpacityMask = settings.EdgeFade > 0
            ? Frozen(new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Colors.White, 1 - settings.EdgeFade),
                    new GradientStop(Colors.Transparent, 1),
                },
            })
            : null;

        // Only the margin the placement asks for, and as a plain rectangle: the GPU clips that for
        // nothing, where a shape with the phone cut out of it has to be rasterised on the CPU for
        // every frame the picture changes. The phone itself needs no cut: the viewport window is
        // clipped to the picture and sits on top of this view, so it already covers that rectangle.
        var phone = new RectD(surfacePixels.X / scale, surfacePixels.Y / scale, Math.Max(0, surfacePixels.Width / scale), Math.Max(0, surfacePixels.Height / scale));
        var region = AmbientLayout.Region(settings.Placement, phone, width, height);
        _container.Clip = region.X <= 0 && region.Y <= 0 && region.Width >= width && region.Height >= height
            ? null
            : Frozen(new RectangleGeometry(new Rect(region.X, region.Y, region.Width, region.Height)));
    }

    /// <summary>A freezable the render thread can share instead of copying it on every frame.</summary>
    private static T Frozen<T>(T value) where T : Freezable
    {
        if (value.CanFreeze)
        {
            value.Freeze();
        }

        return value;
    }

    private static double Finite(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
