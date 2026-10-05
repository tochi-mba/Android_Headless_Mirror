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
            if (e.NewValue is true && (_rows.Count == 0 || Target()?.Serial != _loadedSerial))
            {
                _ = ReloadAsync();
            }
        };
    }

    /// <summary>The phone the list was read from; another phone in use means it is read again.</summary>
    private string? _loadedSerial;

    /// <summary>The phone in use changed (two phones): a list on screen is read again from it, one hidden waits until shown.</summary>
    public void TargetChanged()
    {
        if (IsVisible && Target()?.Serial != _loadedSerial)
        {
            _ = ReloadAsync();
        }
    }

    private string CurrentNamespace => (Namespace.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";

    /// <summary>The phone in use: the mirrored one, or the one beside while its view is in use.</summary>
    private (AdbClient Adb, string Serial)? Target() => _window?.TargetPhone;

    private async Task ReloadAsync()
    {
        if (Target() is not { } target)
        {
            Status.Text = "Connect a phone first.";
            return;
        }

        var (ok, error, rows) = await target.Adb.ListSettingsAsync(target.Serial, CurrentNamespace);
        _loadedSerial = target.Serial;
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

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void OnFieldText(object sender, TextChangedEventArgs e)
    {
        KeyHint.Visibility = Key.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ValueHint.Visibility = Value.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The list scrolls itself, but at either end the wheel belongs to the panel again: otherwise
    /// the pointer resting on the list pins the whole tab in place.
    /// </summary>
    private void OnRowsWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (FindScrollViewer(Rows) is not { } list || !WheelPassesThrough(e.Delta, list.VerticalOffset, list.ScrollableHeight))
        {
            return;
        }

        e.Handled = true;
        RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = this,
        });
    }

    /// <summary>Whether a wheel turn is for the panel: the list cannot move any further that way.</summary>
    internal static bool WheelPassesThrough(int delta, double offset, double scrollable) =>
        scrollable <= 0 || (delta > 0 && offset <= 0) || (delta < 0 && offset >= scrollable);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private async void OnReload(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is AndroidSettingRow row)
        {
            Key.Text = row.Key;
            Value.Text = row.Value;
        }
    }

    /// <summary>
    /// Asks before writing a raw key, in the window rather than in a system dialog, with the risk
    /// shown as a label and the button naming what happens.
    /// </summary>
    private async Task<bool> ConfirmAsync(string ns, string key, string risk, bool always = false, bool deleting = false)
    {
        if (_host is null || _window is null)
        {
            return true;
        }

        if (!always && (!_host.Config.App.ConfirmSensitiveWrites || risk == AndroidSettings.RiskNormal))
        {
            return true;
        }

        _window.ShowTipOnce(Tips.FirstRiskyWrite);
        return await _window.ConfirmAsync(
            deleting ? $"Delete {ns}/{key}?" : $"Change {ns}/{key}?",
            deleting
                ? "Android or the phone maker may put its own default back, which can change how the phone behaves."
                : "This goes straight into Android's settings provider. Android or the phone maker may refuse it or behave differently.",
            deleting ? "Delete the key" : "Write the value",
            risk);
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

        if (Target() is not { } target || !await ConfirmAsync(ns, key, risk))
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
            !await ConfirmAsync(ns, key, risk == AndroidSettings.RiskNormal ? AndroidSettings.RiskAdvanced : risk, always: true, deleting: true))
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
