using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>The Apps tab and favourites: what the list shows, its order, where favourites appear.</summary>
public partial class AppsGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public AppsGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupApps;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        var apps = config.Apps;
        _loading = true;
        try
        {
            AppsShowSystem.IsChecked = apps.ShowSystem;
            AppsShowPackages.IsChecked = apps.ShowPackages;
            SelectTag(AppsOpenOn, apps.OpenOn);
            SelectTag(AppsSortBy, apps.SortBy);
            SelectTag(AppsLayout, apps.Layout);
            AppsShowRecent.IsChecked = apps.ShowRecent;
            AppsRecentCount.Maximum = AppsSettings.MostRecent;
            AppsRecentCount.Value = apps.RecentCount;
            AppsOnControls.IsChecked = apps.FavouritesOnControls;
            AppsOnControlsMost.Maximum = AppsSettings.MostOnControls;
            AppsOnControlsMost.Value = apps.FavouritesOnControlsMost;
            AppsInHud.IsChecked = apps.FavouritesInHud;
            AppsKeys.IsChecked = apps.FavouriteKeys;
            AppsReadOnConnect.IsChecked = apps.ReadOnConnect;
            AppsOpenFresh.IsChecked = apps.OpenFresh;
            AppsCloseOnStop.IsChecked = apps.CloseWhenMirrorStops;

            var on = SettingsDependencies.Of(config);
            AppsRecentOptions.IsEnabled = on.AppsRecent;
            AppsOnControlsOptions.IsEnabled = on.AppsOnControls;

            var hidden = apps.Hidden.Count;
            AppsHiddenText.Text = hidden switch
            {
                0 => "No app is hidden. Hide one from its menu in the Apps tab.",
                1 => "1 app is hidden from the lists.",
                _ => $"{hidden} apps are hidden from the lists.",
            };
            AppsShowHidden.IsEnabled = hidden > 0;
            AutomationProperties.SetHelpText(AppsShowHidden, hidden > 0 ? string.Empty : "No app is hidden.");
            AppsShowHidden.ToolTip = hidden > 0 ? "Every hidden app shows in the Apps tab again" : "No app is hidden.";
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
            c.Apps.ShowSystem = AppsShowSystem.IsChecked == true;
            c.Apps.ShowPackages = AppsShowPackages.IsChecked == true;
            c.Apps.OpenOn = SelectedTag(AppsOpenOn, "phone");
            c.Apps.SortBy = SelectedTag(AppsSortBy, "name");
            c.Apps.Layout = SelectedTag(AppsLayout, "list");
            c.Apps.ShowRecent = AppsShowRecent.IsChecked == true;
            c.Apps.FavouritesOnControls = AppsOnControls.IsChecked == true;
            c.Apps.FavouritesInHud = AppsInHud.IsChecked == true;
            c.Apps.FavouriteKeys = AppsKeys.IsChecked == true;
            c.Apps.ReadOnConnect = AppsReadOnConnect.IsChecked == true;
            c.Apps.OpenFresh = AppsOpenFresh.IsChecked == true;
            c.Apps.CloseWhenMirrorStops = AppsCloseOnStop.IsChecked == true;
        });
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders fire while their XAML is loading; the value labels are created later.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var (recent, most) = ((int)Math.Round(AppsRecentCount.Value), (int)Math.Round(AppsOnControlsMost.Value));
        _host.PreviewConfig(c =>
        {
            c.Apps.RecentCount = recent;
            c.Apps.FavouritesOnControlsMost = most;
        });
    }

    private void OnShowHidden(object sender, RoutedEventArgs e) => _panel?.Save(c => c.Apps.Hidden.Clear());

    private void ShowValues()
    {
        var recent = (int)Math.Round(AppsRecentCount.Value);
        AppsRecentCountValue.Text = recent == 0 ? "None" : recent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var most = (int)Math.Round(AppsOnControlsMost.Value);
        AppsOnControlsMostValue.Text = most == 1 ? "1 favourite" : $"{most} favourites";
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
}
