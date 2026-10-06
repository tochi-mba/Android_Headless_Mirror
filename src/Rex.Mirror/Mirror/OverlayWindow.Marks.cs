using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Marks over several views: a name at the top of each, and an outline around the one that has the
/// keyboard. Static elements, drawn only when they change; nothing here animates or takes the mouse.
/// Names asked for only briefly say themselves for <see cref="CaptionsFor"/> after the views change,
/// then step aside: they sit over the top of each picture, where the phone shows its own status bar.
/// </summary>
public sealed partial class OverlayWindow
{
    public static readonly TimeSpan CaptionsFor = TimeSpan.FromSeconds(4);

    private readonly List<UIElement> _marks = [];
    private string _marksShown = string.Empty;
    private DateTime _marksChangedUtc;

    /// <summary>
    /// The views' names and the outline, in the overlay's own units (those of the mirror area);
    /// empty clears them. <paramref name="briefly"/> lets the names go once they have been read.
    /// </summary>
    public void ShowMarks(IReadOnlyList<(Rect Rect, string Caption)> captions, Rect? outline, bool briefly = false)
    {
        var shown = string.Join('|', captions.Select(c => $"{c.Rect}:{c.Caption}")) + "|" + outline;
        if (shown == _marksShown)
        {
            // Called on every frame: past their time, brief names step aside, and nothing else changes.
            var away = briefly && DateTime.UtcNow - _marksChangedUtc >= CaptionsFor;
            foreach (var pill in _marks.OfType<Border>())
            {
                pill.Visibility = away ? Visibility.Collapsed : Visibility.Visible;
            }

            return;
        }

        _marksShown = shown;
        _marksChangedUtc = DateTime.UtcNow;
        foreach (var mark in _marks)
        {
            _canvas.Children.Remove(mark);
        }

        _marks.Clear();
        if (outline is { IsEmpty: false } around)
        {
            var frame = new Rectangle
            {
                Width = Math.Max(0, around.Width - 2),
                Height = Math.Max(0, around.Height - 2),
                Stroke = (Brush)Application.Current.FindResource("Signal"),
                StrokeThickness = 2,
                RadiusX = 4,
                RadiusY = 4,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(frame, around.X + 1);
            Canvas.SetTop(frame, around.Y + 1);
            Add(frame);
        }

        foreach (var (rect, caption) in captions)
        {
            var pill = new Border
            {
                Background = (Brush)Application.Current.FindResource("Panel"),
                BorderBrush = (Brush)Application.Current.FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2, 8, 2),
                Opacity = 0.85,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = caption,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.FindResource("Text"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = Math.Max(40, rect.Width - 40),
                },
            };
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(pill, rect.X + (rect.Width - pill.DesiredSize.Width) / 2);
            Canvas.SetTop(pill, rect.Y + 6);
            Add(pill);
        }

        void Add(UIElement mark)
        {
            _marks.Add(mark);
            _canvas.Children.Add(mark);
        }
    }

    /// <summary>What is marked now, for the status: the captions in order.</summary>
    public IReadOnlyList<string> MarkCaptions => _marks.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text ?? string.Empty).ToArray();

    /// <summary>Whether the views' names are on screen now (brief ones step aside after a few seconds).</summary>
    public bool MarkCaptionsShowing => _marks.OfType<Border>().Any(b => b.Visibility == Visibility.Visible);

    /// <summary>Whether an outline is drawn now.</summary>
    public bool MarkOutlined => _marks.OfType<Rectangle>().Any();
}
