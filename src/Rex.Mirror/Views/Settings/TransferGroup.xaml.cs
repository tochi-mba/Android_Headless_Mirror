using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>Where files go, which need asking, and how transfer progress is shown.</summary>
public partial class TransferGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public TransferGroup() => InitializeComponent();
    Expander ISettingsGroup.Group => GroupFiles;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        var files = config.Transfer;
        _loading = true;
        try
        {
            TransferEnabled.IsChecked = files.Enabled;
            TransferFolder.Text = files.Folder;
            TransferFolderError.Text = string.Empty;
            TransferSortMedia.IsChecked = files.SortMedia;
            Select(TransferFolders, files.Folders);
            TransferInstallApks.IsChecked = files.InstallApks;
            TransferReplace.IsChecked = files.Replace;
            TransferAllowDowngrade.IsChecked = files.AllowDowngrade;
            TransferGrantPermissions.IsChecked = files.GrantPermissions;
            TransferAllowTestApps.IsChecked = files.AllowTestApps;
            TransferConfirmInstall.IsChecked = files.ConfirmInstall;
            TransferConfirmDrops.IsChecked = files.ConfirmDrops;
            Select(TransferConfirmOver, files.ConfirmOverMb.ToString(CultureInfo.InvariantCulture));
            Select(TransferWhenExists, files.WhenNameExists);
            TransferScanMedia.IsChecked = files.ScanMedia;
            TransferOpenAfterInstall.IsChecked = files.OpenAfterInstall;
            TransferShowFolderAfter.IsChecked = files.ShowFolderAfter;
            TransferAtOnce.Value = files.AtOnce;
            TransferTaskbar.IsChecked = files.TaskbarProgress;
            TransferNotifyHidden.IsChecked = files.NotifyWhenHidden;
            TransferCancelOnLeave.IsChecked = files.CancelWhenPhoneLeaves;
            TransferHistory.Value = files.History;
            TransferSendToMenu.IsChecked = files.SendToMenu;
            var on = SettingsDependencies.Of(config);
            TransferOptions.IsEnabled = on.Transfer;
            TransferInstallOptions.IsEnabled = on.TransferInstall;
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
        _panel?.Save(config =>
        {
            var files = config.Transfer;
            files.Enabled = TransferEnabled.IsChecked == true;
            files.SortMedia = TransferSortMedia.IsChecked == true;
            files.Folders = SelectedTag(TransferFolders, "send");
            files.InstallApks = TransferInstallApks.IsChecked == true;
            files.Replace = TransferReplace.IsChecked == true;
            files.AllowDowngrade = TransferAllowDowngrade.IsChecked == true;
            files.GrantPermissions = TransferGrantPermissions.IsChecked == true;
            files.AllowTestApps = TransferAllowTestApps.IsChecked == true;
            files.ConfirmInstall = TransferConfirmInstall.IsChecked == true;
            files.ConfirmDrops = TransferConfirmDrops.IsChecked == true;
            files.ConfirmOverMb = int.Parse(SelectedTag(TransferConfirmOver, "1024"), CultureInfo.InvariantCulture);
            files.WhenNameExists = SelectedTag(TransferWhenExists, "rename");
            files.ScanMedia = TransferScanMedia.IsChecked == true;
            files.OpenAfterInstall = TransferOpenAfterInstall.IsChecked == true;
            files.ShowFolderAfter = TransferShowFolderAfter.IsChecked == true;
            files.TaskbarProgress = TransferTaskbar.IsChecked == true;
            files.NotifyWhenHidden = TransferNotifyHidden.IsChecked == true;
            files.CancelWhenPhoneLeaves = TransferCancelOnLeave.IsChecked == true;
            files.SendToMenu = TransferSendToMenu.IsChecked == true;
        });
    }

    private void OnFolder(object sender, RoutedEventArgs e) => CommitFolder();
    private void OnFolderKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) CommitFolder(); }

    private void CommitFolder()
    {
        if (_loading || _host is null || TransferFolder.Text.Trim().TrimEnd('/') == _host.Config.Transfer.Folder.TrimEnd('/')) return;
        if (TransferSettings.WhyNotFolder(TransferFolder.Text) is { } why)
        {
            TransferFolderError.Text = why;
            return;
        }

        var folder = TransferFolder.Text.Trim();
        TransferFolderError.Text = string.Empty;
        _panel?.Save(config => config.Transfer.Folder = folder);
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var atOnce = (int)Math.Round(TransferAtOnce.Value);
        var history = (int)Math.Round(TransferHistory.Value);
        _host.PreviewConfig(config => { config.Transfer.AtOnce = atOnce; config.Transfer.History = history; });
    }

    private void ShowValues()
    {
        TransferAtOnceValue.Text = ((int)Math.Round(TransferAtOnce.Value)).ToString(CultureInfo.InvariantCulture);
        TransferHistoryValue.Text = ((int)Math.Round(TransferHistory.Value)).ToString(CultureInfo.InvariantCulture);
    }

    private static string SelectedTag(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;
    private static void Select(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
}
