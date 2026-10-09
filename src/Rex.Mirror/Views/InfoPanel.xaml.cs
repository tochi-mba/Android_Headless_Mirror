using System.IO;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>One line of the Info tab: a name, its value, and whether the value is set in the mono face.</summary>
public sealed record InfoRow(string Key, string Value, bool Mono = false);

public partial class InfoPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;

    public InfoPanel()
    {
        InitializeComponent();

        // From the registry the window and the website also read, so it cannot drift; the window's
        // own lists follow the person's keys as soon as the app attaches (ShowKeys).
        ShortcutGestures.ItemsSource = Rows(Shortcuts.All.Where(s => !s.IsKey));
        ShowKeys(new RexConfig());
        HelpWhatsNew.ToolTip = $"What changed in {CommandRouter.AppVersion}, on the website";
        System.Windows.Automation.AutomationProperties.SetName(HelpWhatsNew, $"What's new in {CommandRouter.AppVersion}");
    }

    private static KeyValuePair<string, string>[] Rows(IEnumerable<Shortcut> shortcuts) =>
        shortcuts.Select(s => KeyValuePair.Create(s.Gesture, s.Description)).ToArray();

    /// <summary>
    /// Every key as this person has it: the window's own (numbered runs folded while they follow
    /// their shipped pattern), browse mode's, and the keys from anywhere, or why there are none.
    /// </summary>
    internal void ShowKeys(RexConfig config)
    {
        var effective = Shortcuts.Effective(config);
        // The favourite and profile keys are only listed while their settings have them on.
        bool Kept(Shortcut s) =>
            (config.Apps.FavouriteKeys || Shortcuts.Favourite(s.Id) == 0) && (config.Profiles.Keys || Shortcuts.ProfileNumber(s.Id) == 0);
        ShortcutKeys.ItemsSource = Rows(Shortcuts.Folded(effective.Where(s => s.IsKey && !s.Browse && !s.Global && Kept(s))));
        ShortcutBrowse.ItemsSource = Rows(effective.Where(s => s.Browse));
        // The heading is set in capitals; the key is written the way it is everywhere else.
        var browse = new KeyMap(config.Keys).ChordFor("browse")?.ToString();
        BrowseHeading.Text = browse is null ? "BROWSE MODE" : "BROWSE MODE · " + browse;

        var keys = effective.Where(s => s.Global).ToArray();
        ShortcutGlobal.ItemsSource = Rows(keys);
        ShortcutGlobalNote.Text = !config.GlobalKeys.Enabled
            ? "Off. Turn them on in Settings, Shortcuts from anywhere."
            : keys.Length == 0
                ? "None set. Add them in Settings, Shortcuts from anywhere."
                : "These work while the window is hidden or behind others.";
    }

    /// <summary>The page each Help button opens, and how the status line names it.</summary>
    internal static (string Url, string What) HelpLink(string tag) => tag switch
    {
        "guide" => (SiteLinks.Guide, "the user guide"),
        "settings" => (SiteLinks.SettingsPage, "every setting"),
        "shortcuts" => (SiteLinks.ShortcutsPage, "the keyboard shortcuts"),
        "changelog" => (SiteLinks.ChangelogFor(CommandRouter.AppVersion), "what is new"),
        _ => (SiteLinks.NewIssue(CommandRouter.AppVersion), "a new problem report"),
    };

    private void OnHelpLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            var (url, what) = HelpLink(tag);
            _window?.OpenSitePage(url, what);
        }
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        if (_host is null)
        {
            return;
        }

        var session = _host.Session;
        var rows = new List<InfoRow>();
        // The phone in use: the mirrored one, or the one beside while its view is in use.
        var device = _window?.TargetPhone is { } target ? session.Devices.FirstOrDefault(d => d.Serial == target.Serial) : session.ActiveDevice;
        if ((_window?.TargetIdentity ?? session.Identity) is { } id && device is not null)
        {
            rows.Add(new InfoRow("Name", id.DisplayName));
            rows.Add(new InfoRow("Model", string.IsNullOrWhiteSpace(id.Model) ? "unknown" : id.Model));
            rows.Add(new InfoRow("Android", string.IsNullOrWhiteSpace(id.AndroidVersion) ? "unknown" : $"{id.AndroidVersion} (API {id.ApiLevel})"));
            rows.Add(new InfoRow("Serial", device.Serial, Mono: true));
            rows.Add(new InfoRow("Connection", device.Transport));
            if (id.DisplayWidth > 0)
            {
                rows.Add(new InfoRow("Display", $"{id.DisplayWidth} × {id.DisplayHeight}"));
            }

            if ((_window?.TargetBattery ?? session.Battery) is { } battery)
            {
                rows.Add(new InfoRow("Battery", $"{battery.Level}%{(battery.Charging ? ", charging" : string.Empty)}"));
            }

            var profile = _host.State.GetDevice(device.Serial);
            rows.Add(new InfoRow("Lock type", LockScreenModes.Describe(profile?.LockScreenMode)));
        }
        else if (session.Devices.Count > 0)
        {
            foreach (var d in session.Devices)
            {
                rows.Add(new InfoRow(d.Serial, $"{d.State} ({d.Transport})"));
            }
        }
        else
        {
            rows.Add(new InfoRow("Status", session.Message));
        }

        DeviceRows.ItemsSource = rows;

        ToolRows.ItemsSource = new[]
        {
            new InfoRow("scrcpy", session.Tools is null ? "Not installed" : $"{session.Tools.Version}"),
            new InfoRow("Folder", _host.Paths.Root, Mono: true),
            new InfoRow("Version", CommandRouter.AppVersion, Mono: true),
            new InfoRow("Startup", StartupRegistration.IsEnabled() ? "Starts with Windows" : "Opened by hand"),
        };

        LogText.Text = string.Join(Environment.NewLine, _host.Log.Tail(12));
    }

    private void OnTour(object sender, RoutedEventArgs e) => _window?.StartTour();

    private void OnResetTips(object sender, RoutedEventArgs e)
    {
        _window?.ForgetTips();
        _window?.SetStatus("The one-time hints will be offered again.");
    }

    private void OnOpenCaptures(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            _window?.OpenFolder(_host.Paths.ScreenshotFolder(_host.Config.App.ScreenshotDirectory));
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            _window?.OpenFolder(_host.Paths.Logs);
        }
    }

    private async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var report = await Diagnostics.BuildAsync(_host.Paths, _host.Log, _host.Runner);
            Clipboard.SetText(report.ToText());
            _window?.SetStatus("Diagnostics copied to the clipboard.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _host.Log.Error("Could not build or copy diagnostics", ex);
            _window?.SetStatus("Could not build diagnostics: " + ex.Message, true);
        }
    }
}
