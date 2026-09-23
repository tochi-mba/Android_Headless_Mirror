using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// The first-run tour: one control at a time lit up, with a callout beside it.
///
/// It covers the window and takes the mouse, so the tour cannot be half-followed while clicking
/// through the thing being explained, and every step lands in the same place each time. Where the
/// callout goes is worked out by <see cref="TourLayout"/>, which keeps the awkward cases (a target
/// in a corner, a window barely bigger than the callout) testable without a screen.
/// </summary>
public partial class TourOverlay : UserControl
{
    private const double SpotlightPadding = 8;

    private readonly List<TourStep> _steps = [];
    private Func<string, FrameworkElement?>? _resolve;
    private Action<bool>? _finished;
    private int _index;

    public TourOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place();
    }

    public bool IsRunning => Visibility == Visibility.Visible;

    /// <summary>Which step is showing, counting from one; 0 when the tour is not running.</summary>
    public int StepNumber => IsRunning ? _index + 1 : 0;

    public int StepCount => _steps.Count;

    /// <summary>
    /// Starts the tour. Steps whose target is missing or too small to point at are dropped, so a
    /// narrow window shows a shorter tour rather than a callout pointing at nothing.
    /// </summary>
    public void Start(IReadOnlyList<TourStep> steps, Func<string, FrameworkElement?> resolve, Action<bool> finished)
    {
        _resolve = resolve;
        _steps.Clear();

        // The overlay has no size while it is collapsed, and every step is measured against it, so
        // it has to be up and laid out before there is anything to measure.
        Visibility = Visibility.Visible;
        UpdateLayout();

        _steps.AddRange(steps.Where(step => Bounds(step.Target) is { } rect && TourLayout.Fits(rect, ActualWidth, ActualHeight)));
        if (_steps.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            finished(false);
            return;
        }

        _finished = finished;
        _index = 0;
        Show();
    }

    public void Stop(bool completed)
    {
        if (!IsRunning)
        {
            return;
        }

        Visibility = Visibility.Collapsed;
        var finished = _finished;
        _finished = null;
        _steps.Clear();
        finished?.Invoke(completed);
    }

    private void Show()
    {
        var step = _steps[_index];
        StepCounter.Text = $"STEP {_index + 1} OF {_steps.Count}";
        StepTitle.Text = step.Title;
        StepBody.Text = step.Body;
        BackButton.Visibility = _index == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _index == _steps.Count - 1 ? "Done" : "Next";
        AutomationProperties.SetName(Callout, $"{step.Title}. {step.Body} Step {_index + 1} of {_steps.Count}.");
        Place();
        Dispatcher.BeginInvoke(() => NextButton.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Lights the current target and puts the callout where there is room for it.</summary>
    private void Place()
    {
        if (!IsRunning || _steps.Count == 0)
        {
            return;
        }

        // In High Contrast the dimming is dropped: the ring alone marks the step, and the scrim
        // stays in place invisibly so the tour still cannot be clicked through.
        Scrim.Opacity = SystemParameters.HighContrast ? 0 : 1;

        var step = _steps[_index];
        var target = Bounds(step.Target) ?? new RectD(ActualWidth / 2, ActualHeight / 2, 1, 1);
        var spot = TourLayout.Spotlight(target, SpotlightPadding);

        Scrim.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)),
            new RectangleGeometry(new Rect(spot.X, spot.Y, spot.Width, spot.Height), 10, 10));

        Ring.Width = spot.Width;
        Ring.Height = spot.Height;
        Canvas.SetLeft(Ring, spot.X);
        Canvas.SetTop(Ring, spot.Y);

        Callout.Measure(new Size(Callout.Width, double.PositiveInfinity));
        var (rect, _) = TourLayout.Callout(
            spot, Callout.Width, Callout.DesiredSize.Height, ActualWidth, ActualHeight, step.Side);
        Canvas.SetLeft(Callout, rect.X);
        Canvas.SetTop(Callout, rect.Y);
    }

    /// <summary>Where a named element sits inside this overlay, or null when it is not on screen.</summary>
    private RectD? Bounds(string name)
    {
        if (_resolve?.Invoke(name) is not { IsVisible: true } element)
        {
            return null;
        }

        try
        {
            // The overlay is a sibling of the content it points at, not its parent.
            var origin = element.TransformToVisual(this).Transform(new Point());
            return new RectD(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            // The element is not in this visual tree right now, which is the same as not being there.
            return null;
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count - 1)
        {
            Stop(completed: true);
            return;
        }

        _index++;
        Show();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_index == 0)
        {
            return;
        }

        _index--;
        Show();
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Stop(completed: true);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsRunning)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Stop(completed: true);
                e.Handled = true;
                break;
            case Key.Right or Key.Enter:
                OnNext(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Left:
                OnBack(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            default:
                base.OnPreviewKeyDown(e);
                break;
        }
    }
}
