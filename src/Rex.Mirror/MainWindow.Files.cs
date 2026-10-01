using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using Microsoft.Win32;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services.Files;

namespace Rex.Mirror;

/// <summary>Every way files enter the window, and the progress they show while reaching the phone.</summary>
public partial class MainWindow
{
    private FileSender? _files;
    private bool _filesArmed;
    private string _filesHint = string.Empty;
    private IReadOnlyList<string> _pendingLaunchFiles = [];
    private ClashAnswer? _nameClashForBatch;

    internal IReadOnlyList<TransferJob> Transfers => _files?.Jobs ?? [];
    internal bool FilesArmed => _filesArmed;
    internal string FilesHint => _filesHint;

    private void InitFiles()
    {
        _files = new FileSender(_host);
        _files.AskNameClash = job => Dispatcher.InvokeAsync(() => AskNameClashAsync(job)).Task.Unwrap();
        _files.Changed += () => Dispatcher.BeginInvoke(RefreshFiles);
        _files.Completed += job => Dispatcher.BeginInvoke(() => OnFileCompleted(job));
        _overlay.FilesDropped += paths => Dispatcher.BeginInvoke(async () => await SendPathsAsync(paths, dropped: true));
        _overlay.FilesDragLeft += () => Dispatcher.BeginInvoke(DisarmFiles);
        _host.Session.Changed += OnFilesPhonesChanged;
        RootGrid.AllowDrop = _host.Config.Transfer.Enabled;
        RootGrid.DragEnter += OnFilesDrag;
        RootGrid.DragOver += OnFilesDrag;
        RootGrid.DragLeave += (_, _) => DisarmFiles();
        RootGrid.Drop += OnFilesDrop;
        TaskbarItemInfo = new TaskbarItemInfo();
        ApplyFilesConfig();
    }

    internal int QueueLaunchFiles(IReadOnlyList<string> paths)
    {
        if (!_host.Config.Transfer.Enabled)
        {
            SetStatus("Sending files from this PC is turned off in Settings.", isError: true);
            return 0;
        }

        _pendingLaunchFiles = _pendingLaunchFiles.Concat(paths).ToArray();
        SendPendingFilesIfReady();
        if (_pendingLaunchFiles.Count > 0) SetStatus("Waiting for a phone to send the files…");
        return paths.Count;
    }

    private void SendPendingFilesIfReady()
    {
        if (_pendingLaunchFiles.Count == 0 || AppsSerial is null) return;
        var paths = _pendingLaunchFiles;
        _pendingLaunchFiles = [];
        Dispatcher.BeginInvoke(async () => await SendPathsAsync(paths, dropped: false));
    }

    private void OnFilesPhonesChanged()
    {
        SendPendingFilesIfReady();
        _files?.PhonesChanged(_host.Session.Devices.Where(device => device.IsReady).Select(device => device.Serial).ToHashSet(StringComparer.Ordinal));
    }

    private static string[] FileDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];

    private void OnFilesDrag(object sender, DragEventArgs e)
    {
        var paths = FileDrop(e.Data);
        if (!_host.Config.Transfer.Enabled || paths.Length == 0 || AppsSerial is not { } serial)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var items = TransferPlan.Plan(paths.Select(LocalEntry.Read).Where(entry => entry is not null).Cast<LocalEntry>(), _host.Config.Transfer);
        var phone = _host.Session.Identity?.DisplayName ?? serial;
        _filesArmed = true;
        _filesHint = TransferPlan.Describe(items, phone);
        _overlay.ArmFiles(true, _filesHint);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnFilesDrop(object sender, DragEventArgs e)
    {
        var paths = FileDrop(e.Data);
        DisarmFiles();
        if (paths.Length > 0)
        {
            await SendPathsAsync(paths, dropped: true);
        }

        e.Handled = true;
    }

    private void DisarmFiles()
    {
        _filesArmed = false;
        _filesHint = string.Empty;
        _overlay.ArmFiles(false);
    }

    private void ApplyFilesConfig()
    {
        RootGrid.AllowDrop = _host.Config.Transfer.Enabled;
        if (!RootGrid.AllowDrop)
        {
            DisarmFiles();
            _pendingLaunchFiles = [];
        }
        try
        {
            SendToMenu.Apply(_host.Config.Transfer.Enabled && _host.Config.Transfer.SendToMenu, _host.ExecutablePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            _host.Log.Error("Could not update File Explorer's Send to shortcut", ex);
            SetStatus("Could not update File Explorer's Send to menu: " + ex.Message, isError: true);
        }
    }

    internal async Task SendFilesAsync()
    {
        var picker = new OpenFileDialog { Title = "Send files to the phone", Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) == true)
        {
            await SendPathsAsync(picker.FileNames, dropped: false);
        }
    }

    internal async Task SendCopiedFilesAsync()
    {
        try
        {
            StringCollection copied = Clipboard.GetFileDropList();
            if (copied.Count == 0)
            {
                SetStatus("There are no copied files. Copy files in File Explorer first.", isError: true);
                return;
            }

            await SendPathsAsync(copied.Cast<string>().ToArray(), dropped: false);
        }
        catch (COMException)
        {
            SetStatus("Another program is using the clipboard. Try again in a moment.", isError: true);
        }
    }

    internal async Task<int> SendPathsAsync(IReadOnlyList<string> paths, bool dropped)
    {
        DisarmFiles();
        if (AppsSerial is not { } serial || _files is null)
        {
            SetStatus("Connect a phone first.", isError: true);
            return 0;
        }

        var settings = _host.Config.Transfer.Copy();
        if (!settings.Enabled)
        {
            SetStatus("Sending files from this PC is turned off in Settings.", isError: true);
            return 0;
        }

        var entries = await Task.Run(() => paths.Select(LocalEntry.Read).Where(entry => entry is not null).Cast<LocalEntry>().ToArray());
        var items = TransferPlan.Plan(entries, settings);
        var accepted = items.Where(item => item.Kind != TransferKind.Refuse).ToArray();
        if (accepted.Length == 0)
        {
            SetStatus(items.FirstOrDefault()?.Why ?? "There is nothing to send.", isError: true);
            return 0;
        }

        var total = accepted.Sum(item => item.Entry.Size);
        var mustAsk = dropped && settings.ConfirmDrops ||
            settings.ConfirmInstall && accepted.Any(item => item.Kind == TransferKind.Install) ||
            settings.ConfirmOverMb > 0 && total > settings.ConfirmOverMb * 1024L * 1024;
        if (mustAsk && !await ConfirmAsync(
                accepted.Any(item => item.Kind == TransferKind.Install) ? "Install on the phone?" : "Send these files?",
                TransferPlan.Describe(accepted, _host.Session.Identity?.DisplayName ?? serial) + $" · {TransferProgress.Size(total)}",
                accepted.Any(item => item.Kind == TransferKind.Install) ? "Install" : "Send"))
        {
            return 0;
        }

        _nameClashForBatch = null;
        _files.Enqueue(serial, items);
        SetStatus(accepted.Length == 1 ? $"Sending {accepted[0].Entry.Name}…" : $"Sending {accepted.Length} items…");
        return accepted.Length;
    }

    private async Task<ClashAnswer?> AskNameClashAsync(TransferJob job)
    {
        if (_nameClashForBatch is { } remembered) return remembered;
        var answer = await ConfirmChoiceAsync(
            $"{job.Item.Entry.Name} is already on the phone",
            "Replace the phone's copy, keep both with a numbered name, or skip this file.",
            "Replace", "Keep both", "Skip", "Do the same for the rest");
        ClashAnswer? chosen = answer.Choice switch
        {
            "Replace" => ClashAnswer.Replace,
            "Keep both" => ClashAnswer.KeepBoth,
            "Skip" => ClashAnswer.Skip,
            _ => null,
        };
        if (answer.ApplyToAll) _nameClashForBatch = chosen;
        return chosen;
    }

    internal void CancelTransfer(Guid id) => _files?.Cancel(id);
    internal void RetryTransfer(Guid id) => _files?.Retry(id);
    internal void ClearFinishedTransfers() => _files?.ClearFinished();

    private void RefreshFiles()
    {
        ControlsPanel.RefreshFiles();
        if (TaskbarItemInfo is null) return;
        if (!_host.Config.Transfer.TaskbarProgress)
        {
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
            TaskbarItemInfo.ProgressValue = 0;
            return;
        }
        var running = Transfers.Where(job => job.State is TransferState.Sending or TransferState.Installing).ToArray();
        var failed = Transfers.Any(job => job.State == TransferState.Failed);
        TaskbarItemInfo.ProgressState = running.Length > 0 ? TaskbarItemProgressState.Normal : failed ? TaskbarItemProgressState.Error : TaskbarItemProgressState.None;
        TaskbarItemInfo.ProgressValue = running.Length == 0 ? 0 : running.Average(job => job.Percent) / 100.0;
    }

    private void OnFileCompleted(TransferJob job)
    {
        var words = TransferProgress.Words(job.State, job.Item.Target, job.Why);
        SetStatus($"{job.Item.Entry.Name}: {words}", job.State == TransferState.Failed);
        if (!IsVisible && _host.Config.Transfer.NotifyWhenHidden)
        {
            _host.Tray?.Notify(job.Item.Entry.Name, words);
        }
    }

    private void FollowScrcpyTransfers(ScrcpyProcess process) =>
        process.FileTransfer += line => Dispatcher.BeginInvoke(() => _files?.NoteScrcpy(line, process.Serial));

    private void ReleaseFilesDrag()
    {
        if (_filesArmed && !NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON))
        {
            DisarmFiles();
        }
    }
}
