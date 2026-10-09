using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services;
using Rex.Mirror.Session;
using Rex.Mirror.Views;

namespace Rex.Mirror;

/// <summary>Keeping the overlay on the picture, the zoom and the navigator, and the live pictures (the soft background and the navigator's picture).</summary>
public partial class MainWindow
{
    /// <summary>How often the window keeps the overlay on the picture while it is on screen, and while it is not.</summary>
    internal static readonly TimeSpan FollowingTick = TimeSpan.FromMilliseconds(33), RestingTick = TimeSpan.FromMilliseconds(250);

    /// <summary>The window's tick now: slower while there is nothing on screen to follow.</summary>
    internal TimeSpan OverlayTick => _overlayTimer.Interval;

    private void TrackOverlay()
    {
        // A window being closed has let go of its contents: there is nothing left to measure.
        if (PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        // Hidden or minimised, nothing on screen needs following, and the rest of the tick's work
        // keeps well at four times a second. Showing the window again brings it straight back.
        var onScreen = IsVisible && WindowState != WindowState.Minimized;
        var tick = onScreen ? FollowingTick : RestingTick;
        if (_overlayTimer.Interval != tick)
        {
            _overlayTimer.Interval = tick;
        }

        if (SidebarScroll.IsVisible && _source is not null)
        {
            // Both corners through the screen, so the panel's own scale is counted as well as the DPI.
            _sidebarWheelBounds = new Rect(SidebarScroll.PointToScreen(new Point()),
                SidebarScroll.PointToScreen(new Point(SidebarScroll.ActualWidth, SidebarScroll.ActualHeight)));
        }
        else _sidebarWheelBounds = Rect.Empty;
        ReleaseStaleAltHold();
        ReleaseFilesDrag();
        FitWhenReady();
        OfferUpdate();
        _host.CheckConfigFile();
        TickPhones();
        _host.Profiles.CheckPower();
        // Cheap, and catches what events miss: the window shown before it has a width, a size
        // that settles after the last layout.
        UpdateRoom();
        var visible = PictureShowable && onScreen && Host.HasChild;
        if (visible)
        {
            foreach (var view in AllViews)
            {
                view.SyncChildShape();
            }

            _screenView?.SyncChildShape();
        }

        _overlay.ReleaseStuckDrags();
        // The overlay covers the whole mirror area: with copies the main view is only one cell of it.
        _overlay.Track(visible ? AreaScreenRect() : default, visible);
        UpdateViewMarks(visible);
        // In the window too when asked: the same bar, over the top of the mirror area.
        _overlay.UpdateHud((_fullscreen || _host.Config.Hud.ShowInWindow) && visible, Host.Zoom, _host.Config.Hud);
        if (visible)
        {
            UpdateNavigator();
        }
    }

    private void OnViewChanged()
    {
        var zoomed = Host.View.IsZoomed;
        if (zoomed)
        {
            ShowTipSoon(Tips.FirstZoom);
        }

        ZoomBadge.Visibility = zoomed ? Visibility.Visible : Visibility.Collapsed;
        ZoomText.Text = $"{Host.Zoom * 100:0}%";
        UpdateNavigator();
        ControlsPanel.Refresh();
    }

    /// <summary>
    /// Whether the live pictures are made without a graphics card: the window drawn in software, the
    /// capture copying the screen because the graphics card will not hand it over, or the capture on
    /// Microsoft's software adapter (Remote Desktop, many virtual machines, CI's runners). Each way,
    /// every frame is the processor's work.
    /// </summary>
    internal bool WithoutGraphicsCard => RenderCapability.Tier >> 16 == 0 || _liveCapture.OnProcessor;

    /// <summary>The frames a second the live pictures are captured at now; 0 while none is wanted.</summary>
    internal double LivePictureRate { get; private set; }

    private void UpdateNavigator()
    {
        var view = Host.View;
        var zoom = _host.Config.Zoom;
        var show = (view.IsZoomed || zoom.NavigatorAlways) && zoom.ShowNavigator && Host.HasChild;
        var aspect = view.SurfaceHeight > 0 ? view.SurfaceWidth / view.SurfaceHeight : 0.45;
        _overlay.UpdateNavigator(show, aspect, Host.VisibleFraction(), zoom);
        // A blurred wash is the opposite of what High Contrast is for, so it stands down there.
        var ambient = _host.Config.Ambient.Enabled && Host.HasChild && !SystemParameters.HighContrast &&
                      !(_host.Config.Ambient.WhenZoomed == "hide" && view.IsZoomed);
        Ambient.Update(ambient, PicturesInArea(), _host.Config.Ambient);
        _ambientWanted = ambient;
        _previewWanted = show && zoom.NavigatorPicture;

        // Without a graphics card every frame is the processor's work: slower there, or not at all.
        var software = WithoutGraphicsCard;
        var choice = _host.Config.App.WithoutGraphicsCard;
        var ambientRate = LivePictures.Rate(_host.Config.Ambient.FrameRate, software, choice);
        var previewRate = LivePictures.Rate(zoom.NavigatorFrameRate, software, choice);
        if (ambientRate == 0 && _ambientWanted)
        {
            _ambientWanted = false;
            Ambient.Update(false, PicturesInArea(), _host.Config.Ambient);
        }

        _previewWanted &= previewRate > 0;

        // One capture feeds both pictures, at the faster of the two rates that are in use.
        var rate = Math.Max(_ambientWanted ? ambientRate : 0, _previewWanted ? previewRate : 0);
        LivePictureRate = rate;
        var interval = TimeSpan.FromMilliseconds(1000 / Math.Max(1, rate));
        if (_ambientTimer.Interval != interval) _ambientTimer.Interval = interval;
        if ((_ambientWanted || _previewWanted) && IsVisible && WindowState != WindowState.Minimized && !_fullscreenTransition)
        {
            if (!_ambientTimer.IsEnabled) _ambientTimer.Start();
        }
        else
        {
            _ambientTimer.Stop();
        }

        if (!_ambientWanted || !_ambientTimer.IsEnabled)
        {
            Ambient.SetFrame(null);
        }

        if (!_previewWanted || !_ambientTimer.IsEnabled)
        {
            _overlay.SetNavigatorFrame(null);
        }
    }

    /// <summary>
    /// One frame of the visible mirror surface for the soft background, taken on a worker thread so
    /// the window stays responsive, and never from the phone.
    /// </summary>
    private async void CaptureAmbientFrame()
    {
        if (!Host.HasChild || !_host.Session.IsMirroring)
        {
            return;
        }

        var surface = Host.SurfaceScreenRect;
        var viewport = Host.ViewportScreenRect;
        var visible = new RECT
        {
            Left = Math.Max(surface.Left, viewport.Left),
            Top = Math.Max(surface.Top, viewport.Top),
            Right = Math.Min(surface.Right, viewport.Right),
            Bottom = Math.Min(surface.Bottom, viewport.Bottom),
        };

        var previewWidth = _previewWanted ? _overlay.NavigatorPreviewPixelWidth : 0;
        var result = await _liveCapture.CaptureAsync(visible, _host.Config.Ambient.Blur, _ambientWanted, previewWidth);
        if (result is null || _quitting || !_ambientTimer.IsEnabled)
        {
            return;
        }

        if (result.Ambient is { } ambient && _ambientWanted)
        {
            Ambient.SetFrame(ambient);
        }

        if (result.Preview is { } preview && _previewWanted)
        {
            _overlay.SetNavigatorFrame(preview);
        }
    }
}
