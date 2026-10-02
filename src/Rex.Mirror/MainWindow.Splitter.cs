using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rex.Core;

namespace Rex.Mirror;

/// <summary>
/// The splitter between the phone and the second screen: in the gap between them, dragged with the
/// mouse or moved with the arrow keys when it has focus, put back with a double-click, remembered.
/// </summary>
public partial class MainWindow
{
    /// <summary>How far one arrow key moves the splitter, as a share of the mirror area.</summary>
    private const double SplitStep = 0.02;

    private void AttachSplitter()
    {
        ViewSplitter.DragDelta += OnSplitterDrag;
        ViewSplitter.DragCompleted += (_, _) => SaveSplit();
        ViewSplitter.MouseDoubleClick += (_, _) => SetSplit(0, save: true);
        ViewSplitter.KeyDown += OnSplitterKey;
        Group.Arranged += _ => UpdateScreenSplitter();
    }

    /// <summary>Shows the splitter over the gap while there is a gap to split, and hides it otherwise.</summary>
    private void UpdateScreenSplitter()
    {
        if (ViewSplitter is null)
        {
            return;
        }

        var arrangement = Group.Arrangement;
        if (!_host.Config.Views.Splitter || _screenView is null || arrangement?.Phone is not { } phone)
        {
            ViewSplitter.Visibility = Visibility.Collapsed;
            return;
        }

        // The gap runs from the end of whichever view comes first to the start of the other.
        var screen = arrangement.Screen;
        var phoneFirst = arrangement.Stacked ? phone.Y < screen.Y : phone.X < screen.X;
        var (first, second) = phoneFirst ? (phone, screen) : (screen, phone);
        const double grip = 8;
        ViewSplitter.Visibility = Visibility.Visible;
        if (arrangement.Stacked)
        {
            var gap = Math.Max(0, second.Y - first.Bottom);
            ViewSplitter.Width = Group.ActualWidth;
            ViewSplitter.Height = Math.Max(grip, gap);
            ViewSplitter.Cursor = Cursors.SizeNS;
            Canvas.SetLeft(ViewSplitter, 0);
            Canvas.SetTop(ViewSplitter, first.Bottom + gap / 2 - ViewSplitter.Height / 2);
        }
        else
        {
            var gap = Math.Max(0, second.X - first.Right);
            ViewSplitter.Width = Math.Max(grip, gap);
            ViewSplitter.Height = Group.ActualHeight;
            ViewSplitter.Cursor = Cursors.SizeWE;
            Canvas.SetLeft(ViewSplitter, first.Right + gap / 2 - ViewSplitter.Width / 2);
            Canvas.SetTop(ViewSplitter, 0);
        }
    }

    private void OnSplitterDrag(object sender, DragDeltaEventArgs e)
    {
        if (Group.Arrangement is not { Phone: { } phone } arrangement)
        {
            return;
        }

        // The phone's new extent: it grows towards the splitter's side as the splitter moves away from it.
        var phoneFirst = arrangement.Stacked ? phone.Y < arrangement.Screen.Y : phone.X < arrangement.Screen.X;
        var delta = arrangement.Stacked ? e.VerticalChange : e.HorizontalChange;
        var extent = (arrangement.Stacked ? phone.Height : phone.Width) + (phoneFirst ? delta : -delta);
        SetSplit(ViewsLayout.SplitAt(extent, arrangement.Stacked ? Group.ActualHeight : Group.ActualWidth), save: false);
    }

    private void OnSplitterKey(object sender, KeyEventArgs e)
    {
        if (Group.Arrangement is not { Phone: { } phone } arrangement)
        {
            return;
        }

        var growing = arrangement.Stacked ? e.Key == Key.Down : e.Key == Key.Right;
        var shrinking = arrangement.Stacked ? e.Key == Key.Up : e.Key == Key.Left;
        if (!growing && !shrinking)
        {
            return;
        }

        var phoneFirst = arrangement.Stacked ? phone.Y < arrangement.Screen.Y : phone.X < arrangement.Screen.X;
        var length = arrangement.Stacked ? Group.ActualHeight : Group.ActualWidth;
        var share = (arrangement.Stacked ? phone.Height : phone.Width) / Math.Max(1, length);
        var step = growing == phoneFirst ? SplitStep : -SplitStep;
        SetSplit(Math.Clamp(share + step, 0.05, 0.95), save: true);
        e.Handled = true;
    }

    private void SetSplit(double split, bool save)
    {
        Group.Split = split;
        Group.InvalidateMeasure();
        if (save)
        {
            SaveSplit();
        }
    }

    private void SaveSplit() => _host.State.SetUi(_host.State.Ui with { ViewSplit = Group.Split });
}
