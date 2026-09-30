using System.Windows;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Session;
using Rex.Mirror.Views;

namespace Rex.Mirror;

/// <summary>
/// Copies of the phone: extra live views beside the main one, each its own scrcpy session and each
/// fully controllable, laid out by <see cref="MirrorGroupPanel"/> and run by <see cref="CopiesController"/>.
///
/// The main view (<see cref="Host"/>) stays in charge of everything that is about the picture
/// rather than the phone: zoom, pan, the navigator, the pattern guide and the soft background's
/// source. Copies follow its zoom exactly, so zooming or panning anywhere changes every view the
/// same way, and a gesture made over a copy is played on the main view at the same spot, which is
/// the same spot on the same phone.
/// </summary>
public partial class MainWindow : ICopyViews
{
    private CopiesController? _copies;
    private readonly Dictionary<MirrorHost, int> _copyIndex = [];
    private MirrorHost? _activeView;

    public CopiesController Copies => _copies!;

    public MirrorHost MainView => Host;

    /// <summary>Every view that is embedding a picture: the main one, then the copies in order.</summary>
    internal IEnumerable<MirrorHost> AllViews
    {
        get
        {
            yield return Host;
            if (_copies is not null)
            {
                foreach (var view in _copies.Views)
                {
                    yield return view;
                }
            }
        }
    }

    /// <summary>The view whose phone window last had the keyboard; keys and refocusing go back there.</summary>
    internal MirrorHost ActiveView => _activeView is { HasChild: true } view ? view : Host;

    private void InitCopies()
    {
        _copies = new CopiesController(_host, this);
        _copies.Changed += OnCopiesChanged;
        _copies.Problem += message => SetStatus(message, isError: true);
        Group.Gap = _host.Config.Copies.Gap;
        Group.Arranged += OnGroupArranged;
        Host.ChildFocused += () => _activeView = Host;
        Host.ViewChanged += _ =>
        {
            UpdateGroupAspect();
            FollowMainView();
        };
    }

    public MirrorHost AddCopyView(int index)
    {
        var view = new MirrorHost { MaxZoom = Host.MaxZoom };
        view.ChildFocused += () => _activeView = view;
        _copyIndex[view] = index;
        var position = 1 + _copyIndex.Count(pair => pair.Value < index);
        Group.Children.Insert(Math.Min(position, Group.Children.Count), view);
        return view;
    }

    public void RemoveCopyView(MirrorHost view)
    {
        if (ReferenceEquals(_activeView, view))
        {
            _activeView = Host;
        }

        _copyIndex.Remove(view);
        view.Detach();
        Group.Children.Remove(view);
        view.Dispose();
    }

    /// <summary>
    /// Opens a copy's window right over the cell it is about to be embedded in, so nothing of it
    /// shows anywhere else for the moment before it is taken in.
    /// </summary>
    public (int X, int Y, int Width, int Height)? CopyLaunchRect(MirrorHost view)
    {
        Group.UpdateLayout();
        var rect = view.ViewportScreenRect;
        if (rect.Width < 50 || rect.Height < 50)
        {
            return LaunchRect();
        }

        var fit = ZoomMath.FitRect(rect.Width, rect.Height, 9, 20);
        return (rect.Left + (int)fit.X, rect.Top + (int)fit.Y, Math.Max(200, (int)fit.Width), Math.Max(200, (int)fit.Height));
    }

    public string? WhyNoMoreCopies(int views) =>
        CopiesLayout.WhyNoMore(views, Group.ActualWidth, Group.ActualHeight, Group.Aspect, Group.Gap, _host.Config.Copies.Most + 1);

    /// <summary>Where the copies stand, for the Controls tab and the status command.</summary>
    internal CopiesStatus CopiesState => new(
        _host.Session.IsMirroring,
        _copies?.Wanted ?? 0,
        _copies?.Running ?? 0,
        _copies?.Starting ?? false,
        Group.Shown,
        CopiesLayout.IsUpright(Group.Aspect),
        _host.Session.IsMirroring ? WhyNoMoreCopies(1 + (_copies?.Wanted ?? 0)) : "Copies need the phone to be mirrored first.");

    /// <summary>Settings that change how copies look apply at once; the controller handles the rest.</summary>
    private void ApplyCopiesConfig()
    {
        PreviewCopies();
        _copies?.ApplyConfig();
    }

    /// <summary>The gap and the zoom limit follow a slider as it moves, before anything is saved.</summary>
    private void PreviewCopies()
    {
        var gap = _host.Config.Copies.Gap;
        if (Math.Abs(Group.Gap - gap) > 0.01)
        {
            Group.Gap = gap;
            Group.InvalidateMeasure();
        }

        foreach (var view in AllViews)
        {
            view.MaxZoom = Host.MaxZoom;
        }
    }

    private void OnCopiesChanged()
    {
        if (_quitting)
        {
            return;
        }

        Group.InvalidateMeasure();
        ControlsPanel.Refresh();
        TrackOverlay();
    }

    /// <summary>Takes the picture's shape from the main view, which decides whether copies show at all.</summary>
    private void UpdateGroupAspect()
    {
        var aspect = Host.VideoAspect;
        if (Math.Abs(Group.Aspect - aspect) > 0.001)
        {
            Group.Aspect = aspect;
            Group.InvalidateMeasure();
        }
    }

    private void FollowMainView()
    {
        if (_copies is null)
        {
            return;
        }

        foreach (var view in _copies.Views)
        {
            view.Follow(Host);
        }
    }

    /// <summary>
    /// After every layout: each view learns where it sits in the mirror area, copies without room
    /// hide their native windows, and every copy catches up with the main view's zoom once its new
    /// size has reached its window.
    /// </summary>
    private void OnGroupArranged(IReadOnlyList<Rect> shown)
    {
        var scale = VisualTreeHelper.GetDpi(Group).DpiScaleX;
        for (var i = 0; i < Group.Children.Count && i < shown.Count; i++)
        {
            if (Group.Children[i] is MirrorHost view)
            {
                view.AreaOffset = new Point(Math.Round(shown[i].X * scale), Math.Round(shown[i].Y * scale));
            }
        }

        if (_quitting)
        {
            // Closing the window lays it out once more; nothing needs following up after that.
            return;
        }

        UpdateCopiesShown();
        Dispatcher.BeginInvoke(FollowMainView, System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(() => ControlsPanel.Refresh(), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// A copy shows only while the main picture does and the last layout gave it room; otherwise
    /// its native window is hidden, so nothing of it stays painted where it used to be.
    /// </summary>
    private void UpdateCopiesShown()
    {
        var mirroring = _host.Session.IsMirroring && !OnboardingView.IsNeeded(_host);
        for (var i = 1; i < Group.Children.Count; i++)
        {
            if (Group.Children[i] is MirrorHost view)
            {
                view.SetShown(i < Group.Shown && mirroring && view.HasChild);
            }
        }
    }

    /// <summary>The whole mirror area in screen pixels: what the overlay covers.</summary>
    private RECT AreaScreenRect()
    {
        if (!Group.IsVisible || PresentationSource.FromVisual(Group) is null)
        {
            return Host.ViewportScreenRect;
        }

        var origin = Group.PointToScreen(new Point());
        var scale = VisualTreeHelper.GetDpi(Group);
        return new RECT
        {
            Left = (int)Math.Round(origin.X),
            Top = (int)Math.Round(origin.Y),
            Right = (int)Math.Round(origin.X + Group.ActualWidth * scale.DpiScaleX),
            Bottom = (int)Math.Round(origin.Y + Group.ActualHeight * scale.DpiScaleY),
        };
    }

    /// <summary>The view showing at a screen point, main or copy, or null when the point is on none.</summary>
    internal MirrorHost? ViewAt(int x, int y)
    {
        foreach (var view in AllViews)
        {
            if (!view.HasChild)
            {
                continue;
            }

            var rect = view.ViewportScreenRect;
            if (rect.Width > 0 && x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
            {
                return view;
            }
        }

        return null;
    }

    /// <summary>
    /// The same place on the main view as a screen point over a copy (a point already on the main
    /// view comes back unchanged). Every view shows the same phone at the same zoom, so this is the
    /// same spot on the phone.
    /// </summary>
    internal (int X, int Y)? OnMainView(int x, int y)
    {
        var view = ViewAt(x, y);
        if (view is null)
        {
            return null;
        }

        if (ReferenceEquals(view, Host))
        {
            return (x, y);
        }

        var from = view.ViewportScreenRect;
        var to = Host.ViewportScreenRect;
        return CopiesLayout.MapPoint(x, y, from.Left, from.Top, from.Width, from.Height, to.Left, to.Top, to.Width, to.Height);
    }

    /// <summary>Where the pictures are, in mirror-area pixels: the soft background frames them.</summary>
    private RectD PicturesInArea()
    {
        RectD? union = null;
        var shown = Math.Max(1, Group.Shown);
        var index = 0;
        foreach (var view in AllViews)
        {
            if (index++ >= shown || !view.HasChild)
            {
                continue;
            }

            var surface = view.AreaSurfaceRect;
            var viewport = view.AreaViewportRect;
            var left = Math.Max(surface.X, viewport.X);
            var top = Math.Max(surface.Y, viewport.Y);
            var right = Math.Min(surface.Right, viewport.Right);
            var bottom = Math.Min(surface.Bottom, viewport.Bottom);
            if (right <= left || bottom <= top)
            {
                continue;
            }

            var visible = new RectD(left, top, right - left, bottom - top);
            union = union is { } u
                ? new RectD(Math.Min(u.X, visible.X), Math.Min(u.Y, visible.Y),
                    Math.Max(u.Right, visible.Right) - Math.Min(u.X, visible.X), Math.Max(u.Bottom, visible.Bottom) - Math.Min(u.Y, visible.Y))
                : visible;
        }

        return union ?? Host.AreaSurfaceRect;
    }
}
