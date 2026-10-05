using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Rex.Mirror.Mirror;

/// <summary>
/// A screenshot's flash: the view goes pale for a moment and back. One static element shown and
/// taken away again; nothing animates, and it never takes the mouse.
/// </summary>
public sealed partial class OverlayWindow
{
    public static readonly TimeSpan FlashFor = TimeSpan.FromMilliseconds(120);

    private Rectangle? _flash;
    private DispatcherTimer? _flashTimer;

    /// <summary>True while a screenshot's flash shows, for the status command and the tests.</summary>
    public bool Flashing => _flash is not null;

    /// <summary>The time the last flash was shown, for the status command and the tests.</summary>
    public DateTime LastFlashUtc { get; private set; }

    /// <summary>Flashes the given rectangle, in the overlay's own units (those of the mirror area).</summary>
    public void Flash(Rect rect)
    {
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        EndFlash();
        _flash = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = (Brush)Application.Current.FindResource("Text"),
            Opacity = 0.55,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_flash, rect.X);
        Canvas.SetTop(_flash, rect.Y);
        _canvas.Children.Add(_flash);
        LastFlashUtc = DateTime.UtcNow;
        _flashTimer ??= new DispatcherTimer { Interval = FlashFor };
        _flashTimer.Tick -= OnFlashEnded;
        _flashTimer.Tick += OnFlashEnded;
        _flashTimer.Start();
    }

    private void OnFlashEnded(object? sender, EventArgs e) => EndFlash();

    private void EndFlash()
    {
        _flashTimer?.Stop();
        if (_flash is not null)
        {
            _canvas.Children.Remove(_flash);
            _flash = null;
        }
    }
}
