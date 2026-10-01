using System.Runtime.InteropServices;
using System.Windows;
using Rex.Core;
using Rex.Mirror.Mirror;

namespace Rex.Mirror;

/// <summary>
/// The phone's apps in the window: opening one from the Apps tab, a favourite tile or key, the
/// chores in an app's menu, and reading the list once a phone is mirrored.
/// </summary>
public partial class MainWindow
{
    private readonly HashSet<string> _appsReadThisConnection = new(StringComparer.Ordinal);
    private string? _appsShownFor;

    /// <summary>The phone the Apps tab shows: the one mirrored, or the only one ready.</summary>
    internal string? AppsSerial =>
        _host.Session.ActiveDevice?.Serial ?? _host.Session.Devices.FirstOrDefault(d => d.IsReady)?.Serial;

    private void AttachApps()
    {
        AppsPanel.Attach(this, _host);
        _host.Session.MirrorReady += ReadAppsOnConnect;
        _host.Session.AppsChanged += AppsEdited;
        _host.ConfigChanged += AppsEdited;
        _host.Session.Changed += OnPhonesChanged;
        AppsEdited();
    }

    /// <summary>Reads the phone's apps once per connection, after the mirror is up, when the setting asks.</summary>
    private void ReadAppsOnConnect(ScrcpyProcess scrcpy)
    {
        var serial = scrcpy.Serial;
        if (_host.Config.Apps.ReadOnConnect && _appsReadThisConnection.Add(serial))
        {
            _ = _host.Session.ReadAppsAsync(serial);
        }
    }

    /// <summary>A phone that left is read again when it comes back; another phone shows its own apps.</summary>
    private void OnPhonesChanged()
    {
        var here = _host.Session.Devices.Where(d => d.IsReady).Select(d => d.Serial).ToHashSet(StringComparer.Ordinal);
        _appsReadThisConnection.RemoveWhere(s => !here.Contains(s));
        if (AppsSerial != _appsShownFor)
        {
            AppsEdited();
        }
    }

    /// <summary>Everything that shows apps catches up after a star, a move or an app opened.</summary>
    internal void AppsEdited()
    {
        _appsShownFor = AppsSerial;
        AppsPanel.Refresh();
        ControlsPanel.RefreshFavourites();
        ApplyHudFavourites();
    }

    /// <summary>The fullscreen controls offer the favourites (as many as have keys) when the setting asks.</summary>
    private void ApplyHudFavourites() =>
        _overlay.HudFavourites = _host.Config.Apps.FavouritesInHud ? Favourites().Take(Shortcuts.FavouriteKeys).ToArray() : [];

    /// <summary>A button of the fullscreen controls: an action, or a favourite app.</summary>
    private async Task RunHudActionAsync(string id)
    {
        if (!id.StartsWith(FullscreenHud.AppAction, StringComparison.Ordinal))
        {
            await RunActionAsync(id);
        }
        else if (Favourites().FirstOrDefault(a => a.Package == id[FullscreenHud.AppAction.Length..]) is { } app)
        {
            await OpenAppAsync(app, _host.Config.Apps.OpenFresh);
        }
    }

    /// <summary>The phone's favourite apps, in order, with what is known of each.</summary>
    internal IReadOnlyList<PhoneApp> Favourites()
    {
        if (AppsSerial is not { } serial || _host.State.GetDevice(serial) is not { } profile)
        {
            return [];
        }

        var known = (profile.Apps ?? []).ToDictionary(a => a.Package, StringComparer.Ordinal);
        return profile.FavouriteApps.Select(p => known.GetValueOrDefault(p) ?? new PhoneApp(p, p, false)).ToArray();
    }

    /// <summary>Opens an app on the phone, says so, and gives the keyboard back to the phone.</summary>
    internal async Task<AndroidResult> OpenAppAsync(PhoneApp app, bool fresh)
    {
        if (AppsSerial is not { } serial)
        {
            SetStatus("Connect a phone first.", isError: true);
            return AndroidResult.Failure("Connect a phone first.");
        }

        SetStatus($"Opening {app.Name}…");
        var result = await _host.Session.OpenAppAsync(serial, app.Package, fresh);
        if (!result.Ok)
        {
            SetStatus($"Could not open {app.Name}: {result.Text}", isError: true);
            return result;
        }

        var locked = _host.Session.Adb is { } adb && await adb.GetKeyguardStateAsync(serial) == KeyguardState.Locked;
        SetStatus(locked ? $"Opened {app.Name}. Unlock the phone to see it." : $"Opened {app.Name}");
        if (ActiveView.HasChild)
        {
            ActiveView.FocusChild();
        }

        return result;
    }

    /// <summary>Opens favourite <paramref name="number"/> (1 to 9), or says there is no such favourite.</summary>
    private void OpenFavourite(int number)
    {
        var favourites = Favourites();
        if (number > favourites.Count)
        {
            SetStatus($"There is no favourite {number}. Star apps in the Apps tab to give them keys.");
            return;
        }

        _ = OpenAppAsync(favourites[number - 1], _host.Config.Apps.OpenFresh);
    }

    /// <summary>
    /// The chores in an app's menu: its info page, closing it, and (asked first, since they cannot
    /// be undone) clearing its data and uninstalling it. Apps that came with the phone are never uninstalled.
    /// </summary>
    internal async Task AppChoreAsync(string verb, PhoneApp app)
    {
        if (AppsSerial is not { } serial || _host.Session.Adb is not { } adb)
        {
            SetStatus("Connect a phone first.", isError: true);
            return;
        }

        if (verb == "clear" && !await ConfirmAsync($"Clear {app.Name}'s data?",
                "Everything it has stored on the phone is deleted: accounts, settings and files it keeps for itself. It starts as if just installed.",
                "Clear its data", risk: "Cannot be undone"))
        {
            return;
        }

        if (verb == "uninstall" && (app.System || !await ConfirmAsync($"Uninstall {app.Name}?",
                $"{app.Name} and everything it stores are removed from the phone.",
                "Uninstall", risk: "Cannot be undone")))
        {
            return;
        }

        var result = verb switch
        {
            "info" => await adb.OpenAppInfoAsync(serial, app.Package),
            "close" => await adb.ForceStopAsync(serial, app.Package),
            "clear" => await adb.ClearAppDataAsync(serial, app.Package),
            _ => await adb.UninstallAppAsync(serial, app.Package),
        };
        var done = verb switch
        {
            "info" => $"{app.Name}'s info is open on the phone",
            "close" => $"Closed {app.Name}",
            "clear" => $"Cleared {app.Name}'s data",
            _ => $"Uninstalled {app.Name}",
        };
        SetStatus(result.Ok ? done : $"Could not do that to {app.Name}: {result.Text}", !result.Ok);
        if (result.Ok && verb == "uninstall")
        {
            _ = _host.Session.ReadAppsAsync(serial);
        }
    }

    /// <summary>Puts text on the clipboard and says so; another program holding the clipboard is said too.</summary>
    internal void CopyText(string text, string said)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus(said);
        }
        catch (COMException)
        {
            SetStatus("Another program is using the clipboard. Try again in a moment.", isError: true);
        }
    }
}
