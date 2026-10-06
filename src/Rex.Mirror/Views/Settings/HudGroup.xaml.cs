using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

public partial class HudGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public HudGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupHud;

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
            HudEnabled.IsChecked = config.Hud.Enabled;
            HudOptions.IsEnabled = config.Hud.Enabled;
            HudMessages.IsChecked = config.Hud.ShowMessages;
            HudShowInWindow.IsChecked = config.Hud.ShowInWindow;
            HudAutoHide.IsChecked = config.Hud.AutoHide;
            HudKeepOrder.IsChecked = config.Hud.KeepOrder;
            // How long they wait means nothing while they never hide.
            HudDelay.IsEnabled = config.Hud.AutoHide;
            SelectTag(HudPosition, config.Hud.Position);
            HudDraggedRow.Visibility = config.Hud.IsPlaced ? Visibility.Visible : Visibility.Collapsed;
            HudScale.Value = config.Hud.Scale;
            HudOpacity.Value = config.Hud.Opacity;
            HudDelay.Value = config.Hud.HideSeconds;
            BuildButtons(config.Hud);
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnHudChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Hud.Enabled = HudEnabled.IsChecked == true;
        c.Hud.ShowMessages = HudMessages.IsChecked == true;
        c.Hud.ShowInWindow = HudShowInWindow.IsChecked == true;
        c.Hud.AutoHide = HudAutoHide.IsChecked == true;
        c.Hud.KeepOrder = HudKeepOrder.IsChecked == true;
    });

    private void OnHudPosition(object sender, SelectionChangedEventArgs e) => Save(c =>
    {
        c.Hud.Position = SelectedTag(HudPosition, "top");
        c.Hud.X = null;
        c.Hud.Y = null;
    });

    private void OnHudPinBack(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Hud.X = null;
        c.Hud.Y = null;
    });

    private void OnHudSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders fire while their XAML is loading; the value labels are created later.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var scale = Math.Round(HudScale.Value, 2);
        var opacity = Math.Round(HudOpacity.Value, 2);
        var seconds = Math.Round(HudDelay.Value);
        _host.PreviewConfig(c =>
        {
            c.Hud.Scale = scale;
            c.Hud.Opacity = opacity;
            c.Hud.HideSeconds = seconds;
        });
    }

    private void OnResetHud(object sender, RoutedEventArgs e) => Save(c => c.Hud = new HudSettings());

    private void OnHudButton(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleButton { Tag: string id }) return;
        // A button added goes last; without keeping the picked order, Normalize puts them in the catalogue's.
        Save(c => c.Hud.Buttons = c.Hud.Buttons.Contains(id, StringComparer.Ordinal)
            ? [.. c.Hud.Buttons.Where(x => x != id)]
            : [.. c.Hud.Buttons, id]);
    }

    private void BuildButtons(HudSettings hud)
    {
        HudButtonCount.Text = hud.Buttons.Count == 0
            ? "Nothing chosen, so the bar stays empty. Click a chip to add a button."
            : "Click to add or remove. They appear in this order.";
        if (HudButtons.Children.Count == 0)
        {
            foreach (var action in MirrorActions.All)
            {
                var chip = new ToggleButton { Content = action.Label, Tag = action.Id, ToolTip = action.Detail, Style = (Style)FindResource("Chip") };
                System.Windows.Automation.AutomationProperties.SetAutomationId(chip, "hud-button " + action.Id);
                System.Windows.Automation.AutomationProperties.SetName(chip, action.Label);
                chip.Checked += OnHudButton;
                chip.Unchecked += OnHudButton;
                HudButtons.Children.Add(chip);
            }
        }

        foreach (var chip in HudButtons.Children.OfType<ToggleButton>())
        {
            chip.IsChecked = hud.Buttons.Contains((string)chip.Tag!, StringComparer.Ordinal);
        }

        BuildPreview(hud);
    }

    private void BuildPreview(HudSettings hud)
    {
        HudPreview.Children.Clear();
        if (hud.Buttons.Count == 0)
        {
            HudPreview.Children.Add(new TextBlock { Text = "empty", Style = (Style)FindResource("MutedText"), FontSize = 11, Margin = new Thickness(2, 2, 0, 2) });
            return;
        }

        foreach (var action in hud.Buttons.Select(MirrorActions.Find).OfType<MirrorAction>())
        {
            var icon = ActionIcons.For(action.Id);
            object content = icon is not null && TryFindResource(icon) is System.Windows.Media.Geometry geometry
                ? new System.Windows.Shapes.Path { Data = geometry, Stroke = (System.Windows.Media.Brush)FindResource("Text"), StrokeThickness = 1.6, Width = 16, Height = 16, Stretch = System.Windows.Media.Stretch.Uniform }
                : action.Label;
            HudPreview.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Raised"), BorderBrush = (System.Windows.Media.Brush)FindResource("Line"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 4, 4), ToolTip = action.Label,
                Child = content is string text ? new TextBlock { Text = text, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("Text") } : (UIElement)content,
            });
        }
    }

    private void ShowValues()
    {
        HudScaleValue.Text = $"{HudScale.Value * 100:0}%";
        HudOpacityValue.Text = $"{HudOpacity.Value * 100:0}%";
        HudDelayValue.Text = $"{HudDelay.Value:0} s";
    }

    private void Save(Action<RexConfig> mutate)
    {
        if (!_loading) _panel?.Save(mutate);
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;
    private static void SelectTag(ComboBox combo, string tag) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
}
