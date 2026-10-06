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
            SelectTag(Backdrop, config.Mirror.Backdrop);
            SelectTag(WithoutGraphicsCard, app.WithoutGraphicsCard);
            WithoutGraphicsCardHint.Text = "The soft background and the navigator's picture, over Remote Desktop or in a virtual machine, where each frame is drawn by the processor." +
                (MainWindow.DrawnInSoftware ? " This PC draws that way now." : string.Empty);
            PanelScale.Value = app.PanelScale;
            ShowTopBar.IsChecked = app.ShowTopBar;
            BuildQuickButtonChoices(app.TopBarButtons);
            QuickButtonChoices.IsEnabled = app.ShowTopBar;
            BuildExtras(app.TopBarButtons.Where(id => !AppSettings.QuickButtons.Contains(id)).ToArray());
            TopBarExtraList.IsEnabled = app.ShowTopBar;
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
            c.Mirror.Backdrop = (Backdrop.SelectedItem as ComboBoxItem)?.Tag as string ?? "ink";
            c.App.WithoutGraphicsCard = (WithoutGraphicsCard.SelectedItem as ComboBoxItem)?.Tag as string ?? LivePictures.Slower;
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

            // The top bar's own order for its phone buttons, whatever order they were picked in; then the rest.
            c.App.TopBarButtons = [.. AppSettings.QuickButtons.Where(chosen.Contains), .. c.App.TopBarButtons.Where(b => !AppSettings.QuickButtons.Contains(b))];
        });
    }

    /// <summary>The other actions in the top bar, each with Remove, and a list to add one from.</summary>
    private void BuildExtras(IReadOnlyList<string> extras)
    {
        TopBarExtraList.Children.Clear();
        foreach (var id in extras)
        {
            var label = MirrorActions.Find(id)?.Label ?? id;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("GhostButton") };
            AutomationProperties.SetAutomationId(remove, "top-bar-remove-" + id);
            AutomationProperties.SetName(remove, $"Take {label} out of the top bar");
            remove.Click += (_, _) => _panel?.Save(c => c.App.TopBarButtons = [.. c.App.TopBarButtons.Where(b => b != id)]);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            TopBarExtraList.Children.Add(row);
        }

        var choice = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetAutomationId(choice, "top-bar-add-choice");
        AutomationProperties.SetName(choice, "A button to add to the top bar");
        choice.Items.Add(new ComboBoxItem { Content = "Choose an action to add", IsEnabled = false });
        foreach (var action in MirrorActions.All.Where(a => !AppSettings.QuickButtons.Contains(a.Id) && !extras.Contains(a.Id)))
        {
            choice.Items.Add(new ComboBoxItem { Content = action.Label, Tag = action.Id });
        }

        choice.SelectedIndex = 0;
        var room = extras.Count < AppSettings.MostTopBarExtras;
        var add = new Button { Content = "Add", Margin = new Thickness(6, 6, 0, 0), IsEnabled = false };
        AutomationProperties.SetAutomationId(add, "top-bar-add");
        AutomationProperties.SetName(add, "Add the button to the top bar");
        add.ToolTip = room ? null : $"The top bar takes at most {AppSettings.MostTopBarExtras} more buttons.";
        choice.SelectionChanged += (_, _) => add.IsEnabled = room && choice.SelectedItem is ComboBoxItem { Tag: string };
        add.Click += (_, _) =>
        {
            if (choice.SelectedItem is ComboBoxItem { Tag: string chosen })
            {
                _panel?.Save(c => c.App.TopBarButtons = [.. c.App.TopBarButtons, chosen]);
            }
        };
        var adding = new Grid();
        adding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        adding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(add, 1);
        adding.Children.Add(choice);
        adding.Children.Add(add);
        TopBarExtraList.Children.Add(adding);
    }

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
}
