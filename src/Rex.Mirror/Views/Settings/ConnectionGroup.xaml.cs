using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>How phones are found (USB first, how often, how long to wait), phones on Wi-Fi by address, and the log.</summary>
public partial class ConnectionGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private MainWindow? _window;
    private bool _loading;

    public ConnectionGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupConnection;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _window = window;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        _loading = true;
        try
        {
            PreferUsb.IsChecked = config.Session.PreferUsb;
            PollSeconds.Value = config.Session.PollSeconds;
            RetrySeconds.Value = config.Session.RetrySeconds;
            WirelessPort.Text = config.Wireless.Port.ToString(CultureInfo.InvariantCulture);
            LoggingEnabled.IsChecked = config.Logging.Enabled;
            LogSize.SelectedItem = LogSize.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == config.Logging.MaxBytes.ToString(CultureInfo.InvariantCulture))
                ?? LogSize.Items.OfType<ComboBoxItem>().MinBy(i => Math.Abs(long.Parse((string)i.Tag, CultureInfo.InvariantCulture) - config.Logging.MaxBytes));
            LogKeep.Value = Math.Min(config.Logging.KeepFiles, LogKeep.Maximum);
            LoggingOptions.IsEnabled = config.Logging.Enabled;
            ShowHosts(config.Wireless.ManualHosts);
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowValues()
    {
        PollValue.Text = PollSeconds.Value == 1 ? "every second" : $"every {PollSeconds.Value:0} seconds";
        RetryValue.Text = RetrySeconds.Value == 1 ? "1 second" : $"{RetrySeconds.Value:0} seconds";
        KeepValue.Text = LogKeep.Value.ToString("0", CultureInfo.InvariantCulture);
    }

    private void ShowHosts(IReadOnlyList<string> hosts)
    {
        HostsList.Children.Clear();
        HostsEmpty.Visibility = hosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var host in hosts)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = host, FontFamily = (System.Windows.Media.FontFamily)FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 3, 10, 3) };
            AutomationProperties.SetName(remove, "Remove " + host);
            AutomationProperties.SetAutomationId(remove, "host-remove-" + host);
            remove.Click += (_, _) => _panel?.Save(c => c.Wireless.ManualHosts.RemoveAll(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)));
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            HostsList.Children.Add(row);
        }
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.Session.PreferUsb = PreferUsb.IsChecked == true;
            c.Logging.Enabled = LoggingEnabled.IsChecked == true;
            if (LogSize.SelectedItem is ComboBoxItem { Tag: string size })
            {
                c.Logging.MaxBytes = long.Parse(size, CultureInfo.InvariantCulture);
            }
        });
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the XAML loads; the labels follow once attached.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var poll = (int)PollSeconds.Value;
        var retry = (int)RetrySeconds.Value;
        var keep = (int)LogKeep.Value;
        _host.PreviewConfig(c =>
        {
            c.Session.PollSeconds = poll;
            c.Session.RetrySeconds = retry;
            c.Logging.KeepFiles = keep;
        });
    }

    private void OnPort(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;
        if (int.TryParse(WirelessPort.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535)
        {
            _panel?.Save(c => c.Wireless.Port = port);
            return;
        }

        // Not a port: put back the one in use, and say why.
        WirelessPort.Text = _host.Config.Wireless.Port.ToString(CultureInfo.InvariantCulture);
        _window?.SetStatus("The Wi-Fi debugging port is a number from 1 to 65535.", isError: true);
    }

    private void OnPortKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnPort(sender, e);
            e.Handled = true;
        }
    }

    private void OnHostKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddHost(sender, e);
            e.Handled = true;
        }
    }

    private void OnAddHost(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var (address, why) = WirelessAddress.Read(HostToAdd.Text, _host.Config.Wireless.Port);
        HostProblem.Text = why ?? string.Empty;
        HostProblem.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        if (address is null)
        {
            return;
        }

        HostToAdd.Text = string.Empty;
        _panel?.Save(c =>
        {
            if (!c.Wireless.ManualHosts.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                c.Wireless.ManualHosts.Add(address);
            }
        });
    }
}
