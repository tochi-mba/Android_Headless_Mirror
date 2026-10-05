using System.Windows;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror;

/// <summary>
/// The window your way: its two bars, the size of the side panel, fitting it to the phone (by key,
/// button, or as each mirror starts), asking before quitting while a phone is mirrored, and a word
/// from the tray when the mirror stops by itself.
/// </summary>
public partial class MainWindow
{
    /// <summary>Fit the window once this start's picture has its shape, when the setting asks.</summary>
    private bool _fitOnStart;

    internal bool TopBarShowing => TopBar.Visibility == Visibility.Visible;

    internal bool StatusBarShowing => StatusBar.Visibility == Visibility.Visible;

    /// <summary>When a screenshot last flashed the view, for the status command and the tests.</summary>
    internal DateTime LastFlashUtc => _overlay.LastFlashUtc;

    internal double PanelScaleShowing => SidebarContent.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1;

    internal string BackdropShowing => MirrorArea.Background is SolidColorBrush { Color: var c } && c == Colors.Black ? "black" : "ink";

    /// <summary>
    /// What shows around the picture: the mirror area, and every view's own window, in the app's
    /// ink or in black. scrcpy's letterbox follows at its next start (<see cref="ScrcpyArguments.LetterboxFor"/>).
    /// </summary>
    private void ApplyBackdrop()
    {
        var black = _host.Config.Mirror.Backdrop == "black";
        MirrorArea.Background = black ? Brushes.Black : (Brush)FindResource("Ink");
        var viewports = AllViews.Append(_screenView).OfType<Mirror.MirrorHost>()
            .Select(view => view.ViewportHandle).Where(handle => handle != IntPtr.Zero).ToArray();
        Native.NativeMethods.SetViewportBackdrop(viewports, black);
    }

    /// <summary>The top bar and the status bar, as the settings say; fullscreen hides both whatever they say.</summary>
    private void ApplyBars()
    {
        var app = _host.Config.App;
        TopBar.Visibility = app.ShowTopBar && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = app.ShowStatusBar && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The side panel's tabs' content larger or smaller, reflowed in the panel's own width; the tabs keep their size.</summary>
    private void ApplyPanelScale()
    {
        var scale = _host.Config.App.PanelScale;
        if (Math.Abs(PanelScaleShowing - scale) < 0.001)
        {
            return;
        }

        SidebarContent.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>On the window's tick: once the picture of a new start has its shape, fit the window to it.</summary>
    private void FitWhenReady()
    {
        if (!_fitOnStart || !Host.VideoReported || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        _fitOnStart = false;
        var fitted = FitWindowToPhone();
        _host.Log.Info("Fitting the window as the mirror started: " + fitted.Text);
    }

    /// <summary>
    /// Sizes the window so the mirror area takes the shape of what it shows: the phone, its copies
    /// or both phones side by side. A second screen fills whatever room it is given, so with one
    /// open there is no shape to fit.
    /// </summary>
    internal AndroidResult FitWindowToPhone()
    {
        if (_fullscreen)
        {
            return AndroidResult.Failure("Leave fullscreen first; it already fills the display.");
        }

        if (_screenView is not null)
        {
            return AndroidResult.Failure("The second screen fills the room it is given, so there is no shape to fit.");
        }

        if (!Host.HasChild || MirrorArea.ActualWidth <= 0 || MirrorArea.ActualHeight <= 0)
        {
            return AndroidResult.Failure("Nothing is mirrored to fit the window to.");
        }

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
            UpdateLayout();
        }

        var aspects = Group.Other is not null
            ? [TwoPhonesLayout.PairAspect(Group.Aspect, Group.OtherAspect, stacked: _host.Config.Views.Arrangement == "stack")]
            : Enumerable.Repeat(Group.Aspect, CopiesLayout.IsUpright(Group.Aspect) ? Math.Max(1, Group.Shown) : 1).ToArray();
        var room = SystemParameters.WorkArea;
        var (width, height) = WindowFit.Size(
            (ActualWidth, ActualHeight),
            (MirrorArea.ActualWidth, MirrorArea.ActualHeight),
            aspects,
            aspects.Length > 1 ? Group.Gap : 0,
            (room.Width, room.Height),
            (MinWidth, MinHeight));
        Width = width;
        Height = height;
        Left = Math.Clamp(Left, room.Left, Math.Max(room.Left, room.Right - width));
        Top = Math.Clamp(Top, room.Top, Math.Max(room.Top, room.Bottom - height));
        UpdateLayout();
        TrackOverlay();
        return AndroidResult.Success($"The window fits the phone: {width:0} × {height:0}");
    }

    private bool AsksBeforeQuitting => _host.Config.App.ConfirmQuit && _host.Session.IsMirroring && !_quitting;

    /// <summary>
    /// Quits as the tray's Quit and closing the window do: first asking, while a phone is mirrored,
    /// when the setting says so. The command line's quit never asks: nobody is there to answer.
    /// </summary>
    public async Task QuitAskingAsync()
    {
        if (AsksBeforeQuitting)
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                ShowFromTray();
            }

            var phone = _host.Session.Identity?.DisplayName ?? "the phone";
            if (!await ConfirmAsync("Quit Android Headless Mirror?", $"The mirror of {phone} closes, and nothing waits for a phone in the tray.", "Quit"))
            {
                return;
            }
        }

        QuitApplication();
    }

    /// <summary>A screenshot was saved: flash the view that took it, and open it, when the settings say so.</summary>
    internal void AfterScreenshot(string path)
    {
        var app = _host.Config.App;
        if (app.ScreenshotFlash && PictureShowable && IsVisible && ActiveView.ActualWidth > 0)
        {
            var corner = ActiveView.TranslatePoint(new Point(), MirrorArea);
            _overlay.Flash(new Rect(corner, new Size(ActiveView.ActualWidth, ActiveView.ActualHeight)));
        }

        if (app.OpenScreenshots)
        {
            UrlOpener.OpenFile(path, _host.Log);
        }
    }

    /// <summary>The mirror stopped without being asked to: say so from the tray while the window is out of sight.</summary>
    private void OnMirrorEndedByItself(string why)
    {
        if (_host.Config.App.NotifyMirrorStops && (!IsVisible || WindowState == WindowState.Minimized))
        {
            Notify("The mirror stopped", why);
        }
    }
}
