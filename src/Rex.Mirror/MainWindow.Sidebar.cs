using System.Windows;

namespace Rex.Mirror;

/// <summary>
/// The side panel's tabs, and what the panel needs to know about the window that the session does
/// not: whether the picture has been frozen. scrcpy keeps no state anyone can ask for, so the
/// window remembers what was last asked of it, and forgets it with the picture.
/// </summary>
public partial class MainWindow
{
    /// <summary>True from a successful Pause until Resume, or until the mirror ends or starts again.</summary>
    public bool MirrorPaused { get; private set; }

    /// <summary>Remembers what an action did to the picture, whoever asked for it: the panel, the HUD, a key or the command line.</summary>
    internal void NoteAction(string id, bool ok)
    {
        if (!ok || id is not ("pause" or "resume"))
        {
            return;
        }

        SetPaused(id == "pause");
    }

    private void SetPaused(bool paused)
    {
        if (MirrorPaused == paused)
        {
            return;
        }

        MirrorPaused = paused;
        ControlsPanel.Refresh();
    }

    private readonly Dictionary<string, double> _tabOffsets = new(StringComparer.Ordinal);
    private string? _shownTab;

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (ControlsPanel is null)
        {
            return;
        }

        var tab = CurrentTab();
        if (_shownTab is { } previous && previous != tab)
        {
            _tabOffsets[previous] = SidebarScroll.VerticalOffset;
        }

        var restore = _tabOffsets.GetValueOrDefault(tab);
        _shownTab = tab;
        ControlsPanel.Visibility = tab == "controls" ? Visibility.Visible : Visibility.Collapsed;
        PhonePanel.Visibility = tab == "phone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = tab == "info" ? Visibility.Visible : Visibility.Collapsed;
        if (tab == "controls")
        {
            _ = ControlsPanel.RefreshRotationAsync();
        }
        else if (tab == "phone")
        {
            PhonePanel.Refresh();
        }
        else if (tab == "settings")
        {
            SettingsPanel.Refresh();
        }
        else if (tab == "info")
        {
            InfoPanel.Refresh();
        }

        SidebarScroll.ScrollToVerticalOffset(restore);
        Dispatcher.BeginInvoke(() => SidebarScroll.ScrollToVerticalOffset(restore), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private string CurrentTab() =>
        TabPhone.IsChecked == true ? "phone" : TabSettings.IsChecked == true ? "settings" : TabInfo.IsChecked == true ? "info" : "controls";

    /// <summary>The tabs in the order Ctrl+Alt+1 to 4 reach them.</summary>
    private static readonly string[] TabOrder = ["controls", "phone", "settings", "info"];

    private void SelectTab(string tab)
    {
        (tab switch
        {
            "phone" => TabPhone,
            "settings" => TabSettings,
            "info" => TabInfo,
            _ => TabControls,
        }).IsChecked = true;
    }
}
