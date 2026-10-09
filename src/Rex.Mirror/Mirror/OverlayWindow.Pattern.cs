using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>Drawing the pattern guide: its dots, the trace and trail while a pattern is drawn, and the spinner while it loads.</summary>
public sealed partial class OverlayWindow
{
    public void ClearPattern()
    {
        _pattern.Children.Clear();
        _patternDots.Clear();
        HidePatternLoading();
        _label.Text = string.Empty;
        ClearTrail();
    }

    public void ShowPatternLoading(string label)
    {
        _pattern.Children.Clear();
        _patternDots.Clear();
        ClearTrail();
        _label.Text = "PATTERN GUIDE  ·  aligning with the phone…";
        _label.Foreground = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F));
        _patternLoadingText.Text = label;
        SetSpinning(true);
        _patternLoading.Visibility = Visibility.Visible;
        Canvas.SetLeft(_patternLoading, Math.Max(12, (_canvas.Width - _patternLoading.Width) / 2));
        Canvas.SetTop(_patternLoading, Math.Max(48, (_canvas.Height - _patternLoading.Height) / 2));
    }

    /// <summary>Draws nine dots (in overlay pixel coordinates) plus an optional calibration box.</summary>
    public void DrawPattern(IReadOnlyList<PointD> pointsPixels, double radiusPixels, double opacity, string label, bool calibrating)
    {
        HidePatternLoading();
        _pattern.Children.Clear();
        _patternDots.Clear();
        var scale = 1.0 / _dpiScale;
        var radius = Math.Max(5, radiusPixels * scale);
        var signal = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F));
        var live = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x4D));

        if (calibrating && pointsPixels.Count == 9)
        {
            var left = pointsPixels.Min(p => p.X) * scale;
            var top = pointsPixels.Min(p => p.Y) * scale;
            var box = new Rectangle
            {
                Width = Math.Max(1, pointsPixels.Max(p => p.X) * scale - left),
                Height = Math.Max(1, pointsPixels.Max(p => p.Y) * scale - top),
                Stroke = live,
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 4],
                Opacity = 0.9,
            };
            Canvas.SetLeft(box, left);
            Canvas.SetTop(box, top);
            _pattern.Children.Add(box);
        }

        foreach (var point in pointsPixels)
        {
            var dot = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = signal,
                StrokeThickness = Math.Max(2, radius * 0.22),
                Fill = Brushes.Transparent,
                Opacity = opacity,
            };
            Canvas.SetLeft(dot, point.X * scale - radius);
            Canvas.SetTop(dot, point.Y * scale - radius);
            _pattern.Children.Add(dot);
            _patternDots.Add(dot);
        }

        _label.Text = label;
        _label.Foreground = calibrating ? live : signal;
    }

    public void UpdatePatternTrace(
        IReadOnlyList<PointD> pointsPixels,
        IReadOnlyList<int> selected,
        PointD? pointerPixels,
        double opacity)
    {
        if (selected.Count == 0 && pointerPixels is null)
        {
            ClearTrail();
            return;
        }

        _trailEmpty = false;
        _trail.Points.Clear();
        foreach (var index in selected)
        {
            if (index >= 0 && index < pointsPixels.Count)
            {
                _trail.Points.Add(new Point(pointsPixels[index].X / _dpiScale, pointsPixels[index].Y / _dpiScale));
            }
        }

        _trail.Opacity = opacity;
        for (var i = 0; i < _patternDots.Count; i++)
        {
            _patternDots[i].Fill = selected.Contains(i) ? _patternDots[i].Stroke : Brushes.Transparent;
        }

        if (selected.Count > 0 && pointerPixels is { } pointer && selected[^1] < pointsPixels.Count)
        {
            var last = pointsPixels[selected[^1]];
            _trailTail.X1 = last.X / _dpiScale;
            _trailTail.Y1 = last.Y / _dpiScale;
            _trailTail.X2 = pointer.X / _dpiScale;
            _trailTail.Y2 = pointer.Y / _dpiScale;
            _trailTail.Opacity = opacity * 0.8;
            _trailTail.Visibility = Visibility.Visible;
        }
        else
        {
            _trailTail.Visibility = Visibility.Collapsed;
        }
    }

    public void ClearTrail()
    {
        // Clearing what is already clear still marks the window as changed, and this runs twenty
        // times a second whenever the pattern guide is off, which is nearly always.
        if (_trailEmpty)
        {
            return;
        }

        _trailEmpty = true;
        _trail.Points.Clear();
        _trailTail.Visibility = Visibility.Collapsed;
        foreach (var dot in _patternDots)
        {
            dot.Fill = Brushes.Transparent;
        }
    }

    private void HidePatternLoading()
    {
        _patternLoading.Visibility = Visibility.Collapsed;
        SetSpinning(false);
    }

    /// <summary>True while the spinner's animations are running; exposed for tests.</summary>
    public bool PatternSpinnerRunning { get; private set; }

    /// <summary>
    /// Starts or stops the loading spinner. A WPF animation keeps the window's render loop awake
    /// for as long as it runs, visible or not, and this is a transparent window that Windows redraws
    /// on the CPU; nine dots pulsing forever behind a collapsed panel cost a steady slice of a core
    /// for a spinner nobody could see.
    /// </summary>
    private void SetSpinning(bool spinning)
    {
        if (spinning == PatternSpinnerRunning)
        {
            return;
        }

        PatternSpinnerRunning = spinning;
        var grid = (UniformGrid)((StackPanel)_patternLoading.Child).Children[0];
        foreach (var dot in grid.Children.OfType<Ellipse>())
        {
            dot.BeginAnimation(UIElement.OpacityProperty, spinning
                ? new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(520))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds((int)dot.Tag * 55),
                }
                : null);
        }
    }

    private static (Border Border, TextBlock Text) CreatePatternLoading()
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 70, Height = 70, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 9; i++)
        {
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(6),
                Fill = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
                Opacity = 0.2,
                Tag = i,
            };
            grid.Children.Add(dot);
        }

        var text = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(text);
        return (new Border
        {
            Width = 210,
            Height = 126,
            Padding = new Thickness(16, 12, 16, 10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x08, 0x0A, 0x09)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xD7, 0xFF, 0x3F)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = content,
        }, text);
    }
}
