using Rex.Core;
using System.IO;

namespace Rex.Mirror.Services.Files;

/// <summary>
/// Runs the transfer queue. It owns only REX-started adb processes; cancellation reaches the
/// process runner, which kills that one process tree, and a partial phone file is removed.
/// </summary>
public sealed class FileSender : IDisposable
{
    private readonly AppHost _host;
    private readonly TransferQueue _queue;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _naming = new(1, 1);
    private readonly Dictionary<Guid, string> _serials = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = [];
    private readonly Dictionary<(bool Install, string Path), Guid> _scrcpy = [];
    private readonly HashSet<(string Serial, string Path)> _reserved = [];
    private bool _disposed;

    public FileSender(AppHost host)
    {
        _host = host;
        _queue = new TransferQueue(host.Config.Transfer.AtOnce, host.Config.Transfer.History);
        host.ConfigChanged += Configure;
    }

    public IReadOnlyList<TransferJob> Jobs
    {
        get
        {
            lock (_gate) return _queue.Jobs.ToArray();
        }
    }
    public event Action? Changed;
    public event Action<TransferJob>? Completed;

    /// <summary>Called only for the "ask" name-clash setting. Null means Skip.</summary>
    public Func<TransferJob, Task<ClashAnswer?>>? AskNameClash { get; set; }

    public IReadOnlyList<TransferJob> Enqueue(string serial, IEnumerable<TransferItem> items)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IReadOnlyList<TransferJob> added;
        lock (_gate)
        {
            added = _queue.Add(items);
            foreach (var job in added)
            {
                _serials[job.Id] = serial;
                if (job.Item.Kind == TransferKind.Refuse)
                {
                    _queue.Skip(job.Id, job.Item.Why);
                }
            }
        }

        Changed?.Invoke();
        Pump();
        return added;
    }

    public bool Cancel(Guid id)
    {
        CancellationTokenSource? cancellation;
        lock (_gate) cancellation = _cancellations.GetValueOrDefault(id);
        cancellation?.Cancel();
        bool changed;
        lock (_gate) changed = _queue.Cancel(id);
        if (changed) Changed?.Invoke();
        return changed;
    }

    public bool Retry(Guid id)
    {
        bool changed;
        lock (_gate) changed = _queue.Retry(id);
        if (changed)
        {
            Changed?.Invoke();
            Pump();
        }

        return changed;
    }

    public void PhonesChanged(IReadOnlySet<string> ready)
    {
        if (!_host.Config.Transfer.CancelWhenPhoneLeaves) return;
        (Guid Id, string Serial)[] active;
        lock (_gate)
        {
            active = _serials.Select(pair => (pair.Key, pair.Value))
                .Where(pair => _queue.Jobs.Any(job => job.Id == pair.Key && !TransferProgress.IsFinished(job.State)))
                .ToArray();
        }

        foreach (var (id, serial) in active.Where(pair => !ready.Contains(pair.Serial)))
        {
            Cancel(id);
        }
    }

    public void ClearFinished()
    {
        lock (_gate) _queue.ClearFinished();
        Changed?.Invoke();
    }

    /// <summary>Feeds scrcpy's built-in drop status into the same transfer history.</summary>
    public void NoteScrcpy(ScrcpyArguments.FileTransferLine line, string serial)
    {
        TransferJob? completed = null;
        lock (_gate)
        {
            var key = (line.Install, line.Path);
            if (!_scrcpy.TryGetValue(key, out var id))
            {
                var entry = LocalEntry.Read(line.Path) ?? new LocalEntry(line.Path, Path.GetFileName(line.Path), false, 0);
                var target = line.Target is { Length: > 0 } remote ? RemoteFolder(remote) : _host.Config.Transfer.Folder;
                var item = new TransferItem(entry, line.Install ? TransferKind.Install : TransferKind.Push, target);
                var job = _queue.Add([item])[0];
                id = job.Id;
                _scrcpy[key] = id;
                _serials[id] = serial;
            }

            switch (line.Phase)
            {
                case ScrcpyArguments.FileTransferPhase.Requested:
                    break;
                case ScrcpyArguments.FileTransferPhase.Started:
                    _queue.Start(id, line.Install);
                    break;
                case ScrcpyArguments.FileTransferPhase.Succeeded:
                    _queue.Start(id, line.Install);
                    _queue.Finish(id, true);
                    _scrcpy.Remove(key);
                    break;
                case ScrcpyArguments.FileTransferPhase.Failed:
                    _queue.Start(id, line.Install);
                    _queue.Finish(id, false, line.Install ? "scrcpy could not install it" : "scrcpy could not push it");
                    _scrcpy.Remove(key);
                    break;
            }

            if (line.Phase is ScrcpyArguments.FileTransferPhase.Succeeded or ScrcpyArguments.FileTransferPhase.Failed)
            {
                completed = _queue.Jobs.FirstOrDefault(job => job.Id == id);
            }
        }

        Changed?.Invoke();
        if (completed is not null)
        {
            Completed?.Invoke(completed);
        }
    }

    private static string RemoteFolder(string remotePath)
    {
        var slash = remotePath.LastIndexOf('/');
        return slash >= 0 ? remotePath[..(slash + 1)] : TransferSettings.DefaultFolder;
    }

    private void Configure()
    {
        lock (_gate) _queue.Configure(_host.Config.Transfer.AtOnce, _host.Config.Transfer.History);
        Changed?.Invoke();
        Pump();
    }

    private void Pump()
    {
        List<(TransferJob Job, CancellationToken Token)> starting = [];
        lock (_gate)
        {
            if (_disposed || _host.Session.Adb is null)
            {
                return;
            }

            foreach (var job in _queue.Take())
            {
                var cancellation = new CancellationTokenSource();
                _cancellations[job.Id] = cancellation;
                starting.Add((job, cancellation.Token));
            }
        }

        foreach (var (job, token) in starting) _ = RunAndContinueAsync(job, token);
        Changed?.Invoke();
    }

    private async Task RunAndContinueAsync(TransferJob job, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate) _queue.Cancel(job.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _host.Log.Error("Could not send " + job.Item.Entry.Path, ex);
            lock (_gate) _queue.Finish(job.Id, false, ex.Message);
        }
        finally
        {
            CancellationTokenSource? cancellation;
            bool finished;
            lock (_gate)
            {
                _cancellations.Remove(job.Id, out cancellation);
                finished = TransferProgress.IsFinished(job.State);
            }
            cancellation?.Dispose();
            Changed?.Invoke();
            if (finished)
            {
                Completed?.Invoke(job);
            }

            Pump();
        }
    }

    private async Task RunAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var adb = _host.Session.Adb!;
        var serial = _serials[job.Id];
        var settings = _host.Config.Transfer.Copy();
        if (job.Item.Kind == TransferKind.Install)
        {
            var flags = new InstallFlags(settings.Replace, settings.AllowDowngrade, settings.GrantPermissions, settings.AllowTestApps);
            var installed = await adb.InstallAsync(serial, job.Item.Entry.Path, flags, cancellationToken).ConfigureAwait(false);
            lock (_gate) _queue.Finish(job.Id, installed.Ok, installed.Ok ? null : TransferProgress.Why(installed.Text));
            if (installed.Ok && settings.OpenAfterInstall && ApkManifest.Read(job.Item.Entry.Path) is { } app)
            {
                await adb.LaunchAppAsync(serial, app.Package, fresh: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var folder = job.Item.Target;
        var name = job.Item.Entry.Name;
        string remotePath;
        ClashAnswer answer;
        await _naming.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var names = (await adb.ListNamesAsync(serial, folder, cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            lock (_gate)
            {
                foreach (var reserved in _reserved.Where(item => item.Serial == serial && RemoteFolder(item.Path) == folder))
                {
                    names.Add(Path.GetFileName(reserved.Path));
                }
            }

            answer = NameClash.Decide(settings.WhenNameExists, names.Contains(name));
            if (answer == ClashAnswer.Ask)
            {
                answer = AskNameClash is null ? ClashAnswer.Skip : await AskNameClash(job).ConfigureAwait(false) ?? ClashAnswer.Skip;
            }

            if (answer == ClashAnswer.Skip)
            {
                lock (_gate) _queue.Skip(job.Id, "already there");
                return;
            }

            if (answer == ClashAnswer.KeepBoth)
            {
                name = NameClash.KeepBoth(name, names);
            }

            remotePath = AdbClient.RemotePath(folder, name);
            lock (_gate) _reserved.Add((serial, remotePath));
        }
        finally
        {
            _naming.Release();
        }

        var target = answer == ClashAnswer.KeepBoth ? remotePath : folder;
        ProcessResult result;
        try
        {
            var push = adb.PushAsync(serial, job.Item.Entry.Path, target, job.Item.Entry.Size, cancellationToken);
            while (!push.IsCompleted)
            {
                await Task.WhenAny(push, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)).ConfigureAwait(false);
                if (!job.Item.Entry.IsFolder && !push.IsCompleted && await adb.RemoteSizeAsync(serial, remotePath, cancellationToken).ConfigureAwait(false) is { } sent)
                {
                    lock (_gate) _queue.Progress(job.Id, sent);
                    Changed?.Invoke();
                }
            }

            result = await push.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await adb.DeleteEntryAsync(serial, remotePath, job.Item.Entry.IsFolder, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            lock (_gate) _reserved.Remove((serial, remotePath));
        }

        if (!result.Ok)
        {
            lock (_gate) _queue.Finish(job.Id, false, TransferProgress.Why(result.FailureText));
            return;
        }

        lock (_gate) _queue.Finish(job.Id, true);
        if (settings.ScanMedia && !job.Item.Entry.IsFolder)
        {
            await adb.ScanMediaAsync(serial, remotePath, cancellationToken).ConfigureAwait(false);
        }

        if (settings.ShowFolderAfter)
        {
            await adb.OpenFolderAsync(serial, folder, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource[] cancellations;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            cancellations = _cancellations.Values.ToArray();
            _cancellations.Clear();
        }

        _host.ConfigChanged -= Configure;
        foreach (var cancellation in cancellations)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

    }
}
