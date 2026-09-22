using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>Friendly Android settings, applied over ADB the moment a control changes.</summary>
public partial class PhonePanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;
    private bool _busy;
    private string _loadedSerial = string.Empty;

    public PhonePanel()
    {
        InitializeComponent();
        Brightness.ValueChanged += (_, _) => BrightnessValue.Text = ((int)Brightness.Value).ToString(CultureInfo.InvariantCulture);
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        Advanced.Attach(window, host);
    }

    private (AdbClient Adb, string Serial)? Target()
    {
        var session = _host?.Session;
        var device = session?.ActiveDevice ?? session?.Devices.FirstOrDefault(d => d.IsReady);
        return session?.Adb is null || device is null ? null : (session.Adb, device.Serial);
    }

    public void Refresh(bool force = false)
    {
        var target = Target();
        Unavailable.Visibility = target is null ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is null)
        {
            _loadedSerial = string.Empty;
            return;
        }

        if (!force && target.Value.Serial == _loadedSerial)
        {
            return;
        }

        _ = LoadAsync(target.Value.Adb, target.Value.Serial);
    }

    private async Task LoadAsync(AdbClient adb, string serial)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _loading = true;
        try
        {
            var state = await adb.GetFriendlyStateAsync(serial);
            _loadedSerial = serial;
            Get(state, "Brightness", out var brightness);
            if (int.TryParse(brightness, out var b))
            {
                Brightness.Value = Math.Clamp(b, 1, 255);
            }

            AutoBrightness.IsChecked = Get(state, "BrightnessMode", out var mode) && mode == "1";
            SelectTag(Timeout, Get(state, "ScreenTimeoutMs", out var timeout) ? timeout : "60000");
            var autoRotate = !Get(state, "AutoRotate", out var rotate) || rotate != "0";
            var userRotation = Get(state, "UserRotation", out var ur) ? ur : "0";
            SelectRotation(autoRotate ? "auto" : userRotation);
            SelectTag(FontScale, NormalizeScale(Get(state, "FontScale", out var fs) ? fs : "1.0"));
            SelectTag(Animation, NormalizeScale(Get(state, "WindowAnimation", out var anim) ? anim : "1"));
            ShowTouches.IsChecked = Get(state, "ShowTouches", out var touches) && touches == "1";
            StayAwake.IsChecked = Get(state, "StayAwake", out var awake) && awake != "0" && awake != "null";

            var ui = Get(state, "UiMode", out var night) ? night : string.Empty;
            SelectTag(DarkMode, ui.Contains("yes", StringComparison.OrdinalIgnoreCase) ? "yes" : ui.Contains("no", StringComparison.OrdinalIgnoreCase) ? "no" : "auto");

            WmSize.Text = ExtractSize(Get(state, "WmSize", out var size) ? size : string.Empty);
            WmDensity.Text = ExtractDensity(Get(state, "WmDensity", out var density) ? density : string.Empty);
            Status.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status.Text = "Could not read phone settings: " + ex.Message;
        }
        finally
        {
            _loading = false;
            _busy = false;
        }
    }

    private static bool Get(IReadOnlyDictionary<string, string> state, string key, out string value)
    {
        if (state.TryGetValue(key, out var raw) && raw != "null" && raw.Length > 0)
        {
            value = raw;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeScale(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d switch { <= 0 => "0", < 0.75 => "0.5", < 0.9 => "0.85", < 1.05 => d < 1.0 ? "1.0" : "1", < 1.2 => "1.15", < 1.4 => "1.30", _ => "1.5" }
            : value;

    private static string ExtractSize(string text)
    {
        var over = System.Text.RegularExpressions.Regex.Match(text, @"Override size:\s*(\d+x\d+)");
        if (over.Success)
        {
            return over.Groups[1].Value;
        }

        var physical = System.Text.RegularExpressions.Regex.Match(text, @"Physical size:\s*(\d+x\d+)");
        return physical.Success ? physical.Groups[1].Value : string.Empty;
    }

    private static string ExtractDensity(string text)
    {
        var over = System.Text.RegularExpressions.Regex.Match(text, @"Override density:\s*(\d+)");
        if (over.Success)
        {
            return over.Groups[1].Value;
        }

        var physical = System.Text.RegularExpressions.Regex.Match(text, @"Physical density:\s*(\d+)");
        return physical.Success ? physical.Groups[1].Value : string.Empty;
    }

    private static void SelectTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals((string)item.Tag, tag, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = -1;
    }

    private static string? SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void SelectRotation(string tag)
    {
        foreach (var button in RotationButtons.Children.OfType<RadioButton>())
        {
            button.IsChecked = (string)button.Tag == tag;
        }
    }

    private async Task<bool> ApplyAsync(string id, string value)
    {
        if (_loading || Target() is not { } target || _window is null)
        {
            return false;
        }

        var result = await target.Adb.ApplyFriendlySettingAsync(target.Serial, id, value);
        if (result.Ok)
        {
            Status.Text = string.Empty;
            _window.SetStatus($"Phone: {id} = {value}", false);
            return true;
        }

        var error = result.Text;
        _window.SetStatus(error, true);
        await LoadAsync(target.Adb, target.Serial);
        Status.Text = error;
        return false;
    }

    private async void OnBrightnessCommit(object sender, RoutedEventArgs e) =>
        await ApplyAsync("brightness", ((int)Brightness.Value).ToString(CultureInfo.InvariantCulture));

    private async void OnAutoBrightness(object sender, RoutedEventArgs e) =>
        await ApplyAsync("brightness-mode", AutoBrightness.IsChecked == true ? "1" : "0");

    private async void OnTimeout(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(Timeout) is { } tag)
        {
            await ApplyAsync("screen-timeout-ms", tag);
        }
    }

    private async void OnRotation(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode } || Target() is not { } target || _window is null)
        {
            return;
        }

        var result = await target.Adb.SetRotationOverrideAsync(target.Serial, mode);
        _window.SetStatus(result.Text, !result.Ok);
        if (!result.Ok)
        {
            var error = result.Text;
            await LoadAsync(target.Adb, target.Serial);
            Status.Text = error;
        }
    }

    private async void OnDarkMode(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(DarkMode) is { } tag)
        {
            await ApplyAsync("dark-mode", tag);
        }
    }

    private async void OnFontScale(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(FontScale) is { } tag)
        {
            await ApplyAsync("font-scale", tag);
        }
    }

    private async void OnAnimation(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(Animation) is { } tag)
        {
            await ApplyAsync("animation-scale", tag);
        }
    }

    private async void OnShowTouches(object sender, RoutedEventArgs e) =>
        await ApplyAsync("show-touches", ShowTouches.IsChecked == true ? "1" : "0");

    private async void OnStayAwake(object sender, RoutedEventArgs e) =>
        await ApplyAsync("stay-awake", StayAwake.IsChecked == true ? "7" : "0");

    private async void OnConnectivity(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string spec })
        {
            var parts = spec.Split(':');
            await ApplyAsync(parts[0], parts[1]);
        }
    }

    private async void OnOverrideApply(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string spec })
        {
            return;
        }

        var parts = spec.Split(':');
        var value = parts.Length > 1 ? parts[1] : parts[0] == "wm-size" ? WmSize.Text.Trim() : WmDensity.Text.Trim();
        if (await ApplyAsync(parts[0], value))
        {
            Refresh(force: true);
        }
    }
}
