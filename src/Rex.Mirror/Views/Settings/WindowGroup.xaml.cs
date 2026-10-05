using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// The window's own settings: staying on top, the side panel's side and size, the top bar and its
/// phone buttons, the status bar, where the window opens, fitting it to the phone, and asking
/// before quitting. All of them apply at once.
/// </summary>
public partial class WindowGroup : UserControl, ISettingsGroup
{
    /// <summary>The top bar's phone buttons, in the words the choices show.</summary>
    private static readonly (string Id, string Label)[] QuickButtonLabels =
    [
        ("home", "Home"),
        ("back", "Back"),
        ("recents", "Recent apps"),
        ("sleep", "Screen off"),
        ("screenshot", "Screenshot"),
        ("sound", "Sound on this PC"),
    ];

    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public WindowGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupWindow;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        _loading = true;
        try
        {
            var app = config.App;
            AlwaysOnTop.IsChecked = app.AlwaysOnTop;
            SelectTag(SidebarSide, app.SidebarSide);
            PanelScale.Value = app.PanelScale;
            ShowTopBar.IsChecked = app.ShowTopBar;
            BuildQuickButtonChoices(app.TopBarButtons);
            QuickButtonChoices.IsEnabled = app.ShowTopBar;
            ShowStatusBar.IsChecked = app.ShowStatusBar;
            ShowHints.IsChecked = app.ShowHints;
            ShowHints.IsEnabled = app.ShowStatusBar;
            ShowFrameRate.IsChecked = app.ShowFrameRate;
            ShowFrameRate.IsEnabled = app.ShowStatusBar;
            RememberPlacement.IsChecked = app.RememberPlacement;
            FitWindowOnStart.IsChecked = app.FitWindowOnStart;
            FitWindowHint.Text = $"{Shortcuts.Gesture("fit-window")} fits it any time.";
            ConfirmQuit.IsChecked = app.ConfirmQuit;
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.App.AlwaysOnTop = AlwaysOnTop.IsChecked == true;
            c.App.SidebarSide = (SidebarSide.SelectedItem as ComboBoxItem)?.Tag as string ?? "right";
            c.App.ShowTopBar = ShowTopBar.IsChecked == true;
            c.App.ShowStatusBar = ShowStatusBar.IsChecked == true;
            c.App.ShowHints = ShowHints.IsChecked == true;
            c.App.ShowFrameRate = ShowFrameRate.IsChecked == true;
            c.App.RememberPlacement = RememberPlacement.IsChecked == true;
            c.App.FitWindowOnStart = FitWindowOnStart.IsChecked == true;
            c.App.ConfirmQuit = ConfirmQuit.IsChecked == true;
        });
    }

    /// <summary>The panel's size previews as it is dragged and is written once the drag ends.</summary>
    private void OnScale(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // WPF raises ValueChanged while InitializeComponent is still assigning Minimum.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var scale = Math.Round(PanelScale.Value, 2);
        _host.PreviewConfig(c => c.App.PanelScale = scale);
    }

    private void ShowValues() => PanelScaleValue.Text = $"{PanelScale.Value * 100:0}%";

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
        _panel?.Save(c =>
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

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
}
