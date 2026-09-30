using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// The settings about the window and how it behaves: staying on top, the panel's side, the top
/// bar's phone buttons, the status bar, notifications, screenshots, how zoom answers the wheel,
/// and how far and fast keyboard swipes go. All of them apply at once.
/// </summary>
public partial class SettingsPanel
{
    /// <summary>The top bar's phone buttons, in the words the choices show.</summary>
    private static readonly (string Id, string Label)[] QuickButtonLabels =
    [
        ("home", "Home"),
        ("back", "Back"),
        ("recents", "Recent apps"),
        ("sleep", "Screen off"),
        ("screenshot", "Screenshot"),
    ];

    private void RefreshWindow(RexConfig c)
    {
        AlwaysOnTop.IsChecked = c.App.AlwaysOnTop;
        SelectTag(SidebarSide, c.App.SidebarSide);
        BuildQuickButtonChoices(c.App.TopBarButtons);
        ShowHints.IsChecked = c.App.ShowHints;
        ShowFrameRate.IsChecked = c.App.ShowFrameRate;
        NotifyConnections.IsChecked = c.App.NotifyConnections;
        SelectTag(ScreenshotFormat, c.App.ScreenshotFormat);
        CopyScreenshots.IsChecked = c.App.CopyScreenshots;
        SelectTag(ZoomAnchor, c.Zoom.ZoomAtPointer ? "pointer" : "middle");
        InvertWheel.IsChecked = c.Zoom.InvertWheel;
        ResetOnRotate.IsChecked = c.Zoom.ResetOnRotate;
        SwipeLength.Value = c.Input.SwipeLength;
        SwipeMilliseconds.Value = c.Input.SwipeMilliseconds;
        ShowSwipeValues();
    }

    private void OnWindowChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.App.AlwaysOnTop = AlwaysOnTop.IsChecked == true;
        c.App.SidebarSide = SelectedTag(SidebarSide, "right");
        c.App.ShowHints = ShowHints.IsChecked == true;
        c.App.ShowFrameRate = ShowFrameRate.IsChecked == true;
        c.App.NotifyConnections = NotifyConnections.IsChecked == true;
        c.App.ScreenshotFormat = SelectedTag(ScreenshotFormat, "png");
        c.App.CopyScreenshots = CopyScreenshots.IsChecked == true;
        c.Zoom.ZoomAtPointer = SelectedTag(ZoomAnchor, "pointer") == "pointer";
        c.Zoom.InvertWheel = InvertWheel.IsChecked == true;
        c.Zoom.ResetOnRotate = ResetOnRotate.IsChecked == true;
    });

    private void BuildQuickButtonChoices(IReadOnlyCollection<string> shown)
    {
        if (QuickButtonChoices.Children.Count == 0)
        {
            foreach (var (id, label) in QuickButtonLabels)
            {
                var chip = new ToggleButton { Content = label, Tag = id, Style = (Style)FindResource("Chip") };
                AutomationProperties.SetAutomationId(chip, "quick-button " + id);
                AutomationProperties.SetName(chip, label + " in the top bar");
                chip.Checked += OnQuickButton;
                chip.Unchecked += OnQuickButton;
                QuickButtonChoices.Children.Add(chip);
            }
        }

        foreach (var chip in QuickButtonChoices.Children.OfType<ToggleButton>())
        {
            chip.IsChecked = shown.Contains((string)chip.Tag!);
        }
    }

    private void OnQuickButton(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleButton { Tag: string id } chip)
        {
            return;
        }

        var on = chip.IsChecked == true;
        Save(c =>
        {
            var chosen = c.App.TopBarButtons.ToHashSet(StringComparer.Ordinal);
            if (on)
            {
                chosen.Add(id);
            }
            else
            {
                chosen.Remove(id);
            }

            // The top bar's own order, whatever order they were picked in.
            c.App.TopBarButtons = AppSettings.QuickButtons.Where(chosen.Contains).ToList();
        });
    }

    /// <summary>The swipe sliders take effect with the next swipe, so they preview live and are written after the drag.</summary>
    private void OnSwipeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null)
        {
            return;
        }

        ShowSwipeValues();
        if (_loading)
        {
            return;
        }

        var length = Math.Round(SwipeLength.Value, 2);
        var milliseconds = (int)Math.Round(SwipeMilliseconds.Value);
        _host.PreviewConfig(c =>
        {
            c.Input.SwipeLength = length;
            c.Input.SwipeMilliseconds = milliseconds;
        });
    }

    private void ShowSwipeValues()
    {
        SwipeLengthValue.Text = $"{SwipeLength.Value * 100:0}% of the usual";
        SwipeTimeValue.Text = $"{SwipeMilliseconds.Value:0} ms";
    }
}
