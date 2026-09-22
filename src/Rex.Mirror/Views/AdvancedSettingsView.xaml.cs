using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>Browses and edits the phone's live Settings Provider keys with the protected-key guardrails.</summary>
public partial class AdvancedSettingsView : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private IReadOnlyList<AndroidSettingRow> _rows = [];

    public AdvancedSettingsView()
    {
        InitializeComponent();
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _rows.Count == 0)
            {
                _ = ReloadAsync();
            }
        };
    }

    private string CurrentNamespace => (Namespace.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";

    private (AdbClient Adb, string Serial)? Target()
    {
        var session = _host?.Session;
        var device = session?.ActiveDevice ?? session?.Devices.FirstOrDefault(d => d.IsReady);
        return session?.Adb is null || device is null ? null : (session.Adb, device.Serial);
    }

    private async Task ReloadAsync()
    {
        if (Target() is not { } target)
        {
            Status.Text = "Connect a phone first.";
            return;
        }

        var (ok, error, rows) = await target.Adb.ListSettingsAsync(target.Serial, CurrentNamespace);
        _rows = ok ? rows : [];
        Status.Text = ok ? $"{rows.Count} keys" : error;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = Search.Text.Trim();
        Rows.ItemsSource = string.IsNullOrEmpty(query)
            ? _rows
            : _rows.Where(r => r.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async void OnNamespace(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await ReloadAsync();
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnReload(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is AndroidSettingRow row)
        {
            Key.Text = row.Key;
            Value.Text = row.Value;
        }
    }

    private bool Confirm(string ns, string key, string risk, bool always = false, bool deleting = false)
    {
        if (_host is null)
        {
            return true;
        }

        if (!always && (!_host.Config.App.ConfirmSensitiveWrites || risk == AndroidSettings.RiskNormal))
        {
            return true;
        }

        var action = deleting ? "Delete" : "Change";
        var consequence = deleting
            ? "\n\nDeleting a raw setting may restore an Android or OEM default and can change system behaviour."
            : string.Empty;
        var result = MessageBox.Show(
            $"{action} {ns}/{key}?\n\nRisk: {risk}. Android or the phone maker may refuse or misbehave.{consequence}",
            "Android Headless Mirror",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private async void OnWrite(object sender, RoutedEventArgs e)
    {
        var key = Key.Text.Trim();
        var ns = CurrentNamespace;
        if (!AndroidSettings.IsValidKey(key))
        {
            Status.Text = "Enter a valid key.";
            return;
        }

        var risk = AndroidSettings.Risk(ns, key);
        if (risk == AndroidSettings.RiskProtected)
        {
            Status.Text = $"'{key}' is protected: changing it could cut off ADB.";
            return;
        }

        if (Target() is not { } target || !Confirm(ns, key, risk))
        {
            return;
        }

        var result = await target.Adb.PutSettingAsync(target.Serial, ns, key, Value.Text);
        Status.Text = result.Ok ? $"Wrote {key}." : result.Text;
        _window?.SetStatus(Status.Text, !result.Ok);
        if (result.Ok)
        {
            await ReloadAsync();
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var key = Key.Text.Trim();
        var ns = CurrentNamespace;
        var risk = AndroidSettings.Risk(ns, key);
        if (risk == AndroidSettings.RiskProtected)
        {
            Status.Text = $"'{key}' is protected from deletion.";
            return;
        }

        if (Target() is not { } target ||
            !Confirm(ns, key, risk == AndroidSettings.RiskNormal ? AndroidSettings.RiskAdvanced : risk, always: true, deleting: true))
        {
            return;
        }

        var result = await target.Adb.DeleteSettingAsync(target.Serial, ns, key);
        Status.Text = result.Ok ? $"Deleted {key}." : result.Text;
        _window?.SetStatus(Status.Text, !result.Ok);
        if (result.Ok)
        {
            await ReloadAsync();
        }
    }
}
