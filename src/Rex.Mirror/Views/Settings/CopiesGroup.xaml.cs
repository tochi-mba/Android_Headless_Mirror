using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

public partial class CopiesGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public CopiesGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupCopies;

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
            CopiesMost.Maximum = CopiesSettings.MostUpperBound;
            CopiesGap.Maximum = CopiesSettings.GapUpperBound;
            CopiesMost.Value = config.Copies.Most;
            CopiesGap.Value = config.Copies.Gap;
            SelectTag(CopiesMaxSize, config.Copies.MaxSize.ToString(CultureInfo.InvariantCulture));
            CopiesRemember.IsChecked = config.Copies.Remember;
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnCopiesSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // WPF raises ValueChanged while InitializeComponent is still assigning Minimum. The
        // labels below do not exist until the rest of this XAML has been constructed.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var most = (int)Math.Round(CopiesMost.Value);
        var gap = Math.Round(CopiesGap.Value);
        _host.PreviewConfig(c =>
        {
            c.Copies.Most = most;
            c.Copies.Gap = gap;
        });
    }

    private void OnCopiesOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.Copies.MaxSize = int.TryParse(SelectedTag(CopiesMaxSize, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0;
            c.Copies.Remember = CopiesRemember.IsChecked == true;
        });
    }

    private void ShowValues()
    {
        var most = (int)Math.Round(CopiesMost.Value);
        CopiesMostValue.Text = most == 1 ? "1 copy" : $"{most} copies";
        CopiesGapValue.Text = CopiesGap.Value < 0.5 ? "none" : $"{CopiesGap.Value:0} px";
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
    }
}
