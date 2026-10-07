using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>The second screen's display and placement, and how several views share the mirror area.</summary>
public partial class SecondScreenGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public SecondScreenGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupScreen;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        var screen = config.SecondScreen;
        var views = config.Views;
        _loading = true;
        try
        {
            SelectTag(ScreenPlacement, screen.Placement);
            SelectTag(ScreenSide, screen.Side);
            SelectTag(ScreenSize, screen.Size);
            ScreenCustomWidth.Text = screen.CustomWidth.ToString(CultureInfo.InvariantCulture);
            ScreenCustomHeight.Text = screen.CustomHeight.ToString(CultureInfo.InvariantCulture);
            ScreenPortrait.IsChecked = screen.Portrait;
            ScreenResizeWhileDragging.IsChecked = screen.ResizeWhileDragging;
            ScreenDpi.Value = screen.Dpi == 0 ? ScreenDpi.Minimum : screen.Dpi;
            SelectTag(ScreenMaxSize, screen.MaxSize.ToString(CultureInfo.InvariantCulture));
            ScreenDecorations.IsChecked = screen.Decorations;
            ScreenKeepApps.IsChecked = screen.KeepAppsOnClose;
            SelectTag(ScreenKeyboard, screen.Keyboard);
            ScreenReopen.IsChecked = screen.ReopenOnStart;
            ScreenMinWidth.Value = screen.MinWidth;
            SelectTag(ViewsArrangement, views.Arrangement);
            SelectTag(ViewsOutline, views.Outline);
            SelectTag(ViewsCaptions, views.Captions);
            ViewsSplitter.IsChecked = views.Splitter;

            var on = SettingsDependencies.Of(config);
            ScreenCustomOptions.IsEnabled = on.ScreenCustom;
            ScreenFixedOptions.IsEnabled = on.ScreenUpright;
            ScreenResizeWhileDragging.IsEnabled = !on.ScreenFixed;
            ScreenBesideOptions.IsEnabled = on.ScreenBeside;
            ViewsSplitter.IsEnabled = on.ScreenBeside;
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.SecondScreen.Placement = SelectedTag(ScreenPlacement, "beside");
            c.SecondScreen.Side = SelectedTag(ScreenSide, "right");
            c.SecondScreen.Size = SelectedTag(ScreenSize, "follow");
            c.SecondScreen.Portrait = ScreenPortrait.IsChecked == true;
            c.SecondScreen.ResizeWhileDragging = ScreenResizeWhileDragging.IsChecked == true;
            c.SecondScreen.MaxSize = int.TryParse(SelectedTag(ScreenMaxSize, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0;
            c.SecondScreen.Decorations = ScreenDecorations.IsChecked == true;
            c.SecondScreen.KeepAppsOnClose = ScreenKeepApps.IsChecked == true;
            c.SecondScreen.Keyboard = SelectedTag(ScreenKeyboard, "here");
            c.SecondScreen.ReopenOnStart = ScreenReopen.IsChecked == true;
            c.Views.Arrangement = SelectedTag(ViewsArrangement, "auto");
            c.Views.Outline = SelectedTag(ViewsOutline, "auto");
            c.Views.Captions = SelectedTag(ViewsCaptions, "auto");
            c.Views.Splitter = ViewsSplitter.IsChecked == true;
        });
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders fire while their XAML is loading; the value labels are created later.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var dpi = ScreenDpi.Value <= ScreenDpi.Minimum ? 0 : (int)ScreenDpi.Value;
        var minimum = (int)ScreenMinWidth.Value;
        _host.PreviewConfig(c =>
        {
            c.SecondScreen.Dpi = dpi;
            c.SecondScreen.MinWidth = minimum;
        });
    }

    /// <summary>Your own size is saved once it is typed: on leaving the box, or Enter.</summary>
    private void OnCustomSize(object sender, RoutedEventArgs e)
    {
        if (_loading || _panel is null) return;
        if (!int.TryParse(ScreenCustomWidth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(ScreenCustomHeight.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            // Not a number: back to what is saved.
            Refresh(_host!.Config);
            return;
        }

        _panel.Save(c =>
        {
            c.SecondScreen.CustomWidth = width;
            c.SecondScreen.CustomHeight = height;
        });
    }

    private void OnCustomSizeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnCustomSize(sender, e);
            e.Handled = true;
        }
    }

    private void ShowValues()
    {
        ScreenDpiValue.Text = ScreenDpi.Value <= ScreenDpi.Minimum ? "The phone's own" : $"{ScreenDpi.Value:0} dpi";
        ScreenMinWidthValue.Text = $"{ScreenMinWidth.Value:0} px";
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
}
