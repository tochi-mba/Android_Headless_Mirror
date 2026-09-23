using System.Windows;
using System.Windows.Interop;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// A compact, non-activating host for the fullscreen HUD.
///
/// The general mirror overlay deliberately stays transparent to mouse input so Android receives
/// normal clicks directly. Hosting fullscreen controls in their own tiny HWND avoids DPI-sensitive
/// hit testing across the whole mirror surface and means only the visible HUD can intercept input.
/// </summary>
public sealed class FullscreenHudWindow : Window
{
    private const double TopMargin = 12;

    private readonly Window _owner;
    private readonly FullscreenHud _hud = new();
    private HwndSource? _source;

    public FullscreenHudWindow(Window owner)
    {
        _owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = false;
        SizeToContent = SizeToContent.Manual;
        Width = 1;
        Height = 1;
        Content = _hud;

        _hud.ActionRequested += id => ActionRequested?.Invoke(id);

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            var exStyle = NativeMethods.GetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetStyle(
                _source.Handle,
                NativeMethods.GWL_EXSTYLE,
                (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) |
                NativeMethods.WS_EX_NOACTIVATE |
                NativeMethods.WS_EX_TOOLWINDOW);
            _source.AddHook(WndProcHook);
        };
    }

    public event Action<string>? ActionRequested;

    public bool HudVisible => IsVisible && _hud.IsShown;

    private IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    public void Reveal(string? message = null) => _hud.Reveal(message);

    public void Update(bool enabled, bool pointerInZone, double zoom, RECT mirrorPixels, double dpiScale, HudSettings settings)
    {
        _hud.Apply(settings);
        Opacity = settings.Opacity;
        _hud.Update(enabled, pointerInZone, zoom);

        if (!enabled || mirrorPixels.Width <= 0 || mirrorPixels.Height <= 0)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        EnsureWindow();

        var scale = Math.Max(0.5, dpiScale);
        var maxWidthDip = Math.Max(1, mirrorPixels.Width / scale - (TopMargin * 2));
        _hud.Measure(new Size(maxWidthDip, double.PositiveInfinity));

        var widthDip = Math.Max(1, Math.Min(maxWidthDip, _hud.DesiredSize.Width));
        var heightDip = Math.Max(1, _hud.DesiredSize.Height);
        Width = widthDip;
        Height = heightDip;

        var widthPx = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
        var heightPx = Math.Max(1, (int)Math.Ceiling(heightDip * scale));
        var (left, top) = HudLayout.Anchor(settings.Position, widthPx, heightPx, mirrorPixels.Width, mirrorPixels.Height, TopMargin * scale);
        var leftPx = mirrorPixels.Left + (int)Math.Round(left);
        var topPx = mirrorPixels.Top + (int)Math.Round(top);

        NativeMethods.SetWindowPos(
            Handle,
            IntPtr.Zero,
            leftPx,
            topPx,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOSENDCHANGING |
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void EnsureWindow()
    {
        if (_source is null)
        {
            Owner ??= _owner;
            new WindowInteropHelper(this).EnsureHandle();
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCHITTEST)
        {
            handled = true;
            return new IntPtr(_hud.IsShown ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
        }

        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProcHook);
        base.OnClosed(e);
    }
}
