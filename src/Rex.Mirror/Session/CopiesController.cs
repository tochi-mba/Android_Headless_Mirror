using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Services;

namespace Rex.Mirror.Session;

/// <summary>What the copies need from the window: somewhere to show each one, and room to judge.</summary>
public interface ICopyViews
{
    /// <summary>A new, empty view for copy <paramref name="index"/>, already in the layout.</summary>
    MirrorHost AddCopyView(int index);

    /// <summary>Takes a copy's view out of the layout and frees it.</summary>
    void RemoveCopyView(MirrorHost view);

    /// <summary>Where a copy's window should open before it is embedded (over its own view), or null for scrcpy's choice.</summary>
    (int X, int Y, int Width, int Height)? CopyLaunchRect(MirrorHost view);

    /// <summary>Null when another copy fits now; otherwise why not, in words.</summary>
    string? WhyNoMoreCopies(int views);

    /// <summary>The main view, whose zoom every copy follows.</summary>
    MirrorHost MainView { get; }
}

/// <summary>
/// Runs the copies of the phone: starts them one at a time once the main picture is up, embeds
/// each in its own view, keeps them on the main view's zoom, stops them when they are removed or the
/// main session ends, and tries a failed copy once more before giving it up with the reason.
/// The policy itself is <see cref="CopiesPlan"/>; this class carries it out.
/// </summary>
public sealed class CopiesController : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly AppHost _host;
    private readonly ICopyViews _views;
    private readonly CopiesPlan _plan = new();
    private readonly SortedDictionary<int, RunningCopy> _copies = [];
    private readonly DispatcherTimer _retry;
    private bool _pumping;
    private bool _pumpAgain;
    private bool _disposed;

    /// <summary>A copy that is up, with the copy resolution it was started with.</summary>
    private sealed record RunningCopy(int Index, ScrcpyProcess Process, MirrorHost View, int MaxSize);

    public CopiesController(AppHost host, ICopyViews views)
    {
        _host = host;
        _views = views;
        _retry = new DispatcherTimer { Interval = RetryDelay };
        _retry.Tick += (_, _) =>
        {
            _retry.Stop();
            Pump();
        };
        _plan.GaveUp += (index, reason) =>
        {
            _host.Log.Warn($"Gave up on copy {index + 1}: {reason}");
            Remember();
            Problem?.Invoke("A copy of the phone was closed. " + reason);
        };

        if (host.Config.Copies.Remember)
        {
            _plan.Want(Math.Min(host.State.Ui.Copies, host.Config.Copies.Most));
        }
    }

    /// <summary>How many copies are wanted, not counting the phone's own view.</summary>
    public int Wanted => _plan.Wanted;

    /// <summary>How many copies are running and embedded.</summary>
    public int Running => _copies.Count;

    public bool Starting => _plan.Starting;

    /// <summary>The copies' views, in order.</summary>
    public IEnumerable<MirrorHost> Views => _copies.Values.Select(c => c.View);

    /// <summary>The processes of the running copies, for status and tests.</summary>
    public IEnumerable<ScrcpyProcess> Processes => _copies.Values.Select(c => c.Process);

    public event Action? Changed;

    /// <summary>Raised with a sentence for the person when a copy had to be given up.</summary>
    public event Action<string>? Problem;

    /// <summary>Adds a copy, or says why one cannot be added.</summary>
    public AndroidResult Add()
    {
        if (!_host.Session.IsMirroring)
        {
            return AndroidResult.Failure("Copies need the phone to be mirrored first.");
        }

        if (_views.WhyNoMoreCopies(1 + _plan.Wanted) is { } reason)
        {
            return AndroidResult.Failure(reason);
        }

        _plan.Want(_plan.Wanted + 1);
        Remember();
        Pump();
        return AndroidResult.Success($"Adding copy {_plan.Wanted}…");
    }

    /// <summary>Removes the last copy.</summary>
    public AndroidResult Remove()
    {
        if (_plan.Wanted == 0)
        {
            return AndroidResult.Failure("There is no copy to remove.");
        }

        _plan.Want(_plan.Wanted - 1);
        Remember();
        Pump();
        return AndroidResult.Success(_plan.Wanted == 0 ? "Copies closed." : $"{_plan.Wanted} {(_plan.Wanted == 1 ? "copy" : "copies")} left.");
    }

    /// <summary>Starts or stops copies until what runs matches what is wanted. Safe to call any time.</summary>
    public async void Pump()
    {
        if (_disposed)
        {
            return;
        }

        if (_pumping)
        {
            _pumpAgain = true;
            return;
        }

        _pumping = true;
        try
        {
            do
            {
                _pumpAgain = false;
                while (!_disposed)
                {
                    var step = _plan.Next(_host.Session.IsMirroring);
                    if (step.Kind == CopyStepKind.Wait)
                    {
                        break;
                    }

                    if (step.Kind == CopyStepKind.Stop)
                    {
                        Stop(step.Index);
                        continue;
                    }

                    if (!await LaunchAsync(step.Index).ConfigureAwait(true))
                    {
                        break;
                    }
                }
            }
            while (_pumpAgain && !_disposed);
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task<bool> LaunchAsync(int index)
    {
        _plan.Started(index);
        var maxSize = _host.Config.Copies.MaxSize;
        var view = _views.AddCopyView(index);
        Changed?.Invoke();
        var launch = await _host.Session.LaunchCopyAsync(index, _views.CopyLaunchRect(view)).ConfigureAwait(true);
        if (_disposed || !launch.Started || !_host.Session.IsMirroring)
        {
            launch.Process?.Dispose();
            _views.RemoveCopyView(view);
            if (launch.Started || _disposed)
            {
                // It started, but the main session ended meanwhile: not a failure of the copy.
                _plan.Stopped(index);
            }
            else
            {
                _plan.Failed(index, launch.Reason);
                _retry.Stop();
                _retry.Start();
            }

            Changed?.Invoke();
            return false;
        }

        var scrcpy = launch.Process!;
        view.SetShown(true);
        view.Attach(scrcpy.Hwnd, scrcpy.ThreadId, (uint)scrcpy.ProcessId);
        if (scrcpy.VideoSize is { } size)
        {
            view.SetVideoSize(size.Width, size.Height);
        }

        scrcpy.VideoSizeChanged += (width, height) => view.Dispatcher.BeginInvoke(() =>
        {
            if (_copies.TryGetValue(index, out var copy) && ReferenceEquals(copy.Process, scrcpy))
            {
                view.SetVideoSize(width, height);
                view.Follow(_views.MainView);
            }
        });
        _ = scrcpy.Exited.ContinueWith(_ => view.Dispatcher.BeginInvoke(() => OnExited(index, scrcpy)), TaskScheduler.Default);
        view.Follow(_views.MainView);
        if (_host.Session.ViewPaused && ScrcpyShortcuts.For("pause") is { } pause)
        {
            ScrcpyShortcutSender.Send(scrcpy.Hwnd, pause);
        }
        _copies[index] = new RunningCopy(index, scrcpy, view, maxSize);
        _plan.Ready(index);
        _host.Log.Info($"Copy {index + 1} is showing.");
        Changed?.Invoke();
        return true;
    }

    private void Stop(int index)
    {
        if (!_copies.Remove(index, out var copy))
        {
            _plan.Stopped(index);
            return;
        }

        _plan.Stopped(index);
        copy.View.Detach();
        _views.RemoveCopyView(copy.View);
        copy.Process.Dispose();
        _host.Log.Info($"Copy {index + 1} closed.");
        Changed?.Invoke();
    }

    private void OnExited(int index, ScrcpyProcess scrcpy)
    {
        // A copy that was stopped on purpose has already left the list, so its exit is ignored here.
        if (!_copies.TryGetValue(index, out var copy) || !ReferenceEquals(copy.Process, scrcpy))
        {
            return;
        }

        // It ended on its own. If the whole phone went away, the main session's end stops the rest
        // and nothing is held against the copy; otherwise it gets one more try.
        _copies.Remove(index);
        copy.View.Detach();
        _views.RemoveCopyView(copy.View);
        var reason = SessionController.CopyFailure(scrcpy.RecentStderr);
        scrcpy.Dispose();
        if (_host.Session.IsMirroring)
        {
            _host.Log.Warn($"Copy {index + 1} closed unexpectedly: {reason}");
            _plan.Failed(index, reason);
            _retry.Stop();
            _retry.Start();
        }
        else
        {
            _plan.Stopped(index);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Brings the copies in line with changed settings: a lower limit closes the extra ones, and a
    /// new copy resolution starts the copies again with it. The main picture is never touched.
    /// </summary>
    public void ApplyConfig()
    {
        var settings = _host.Config.Copies;
        if (_plan.Wanted > settings.Most)
        {
            _plan.Want(settings.Most);
        }

        foreach (var copy in _copies.Values.Where(c => c.MaxSize != settings.MaxSize).ToArray())
        {
            _host.Log.Info($"Copy {copy.Index + 1} restarts at the new copy resolution.");
            Stop(copy.Index);
        }

        Remember();
        Pump();
        Changed?.Invoke();
    }

    private void Remember()
    {
        if (_host.Config.Copies.Remember && _host.State.Ui.Copies != _plan.Wanted)
        {
            _host.State.SetUi(_host.State.Ui with { Copies = _plan.Wanted });
        }
    }

    /// <summary>Closes every copy now; used when the app quits.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _retry.Stop();
        foreach (var index in _copies.Keys.ToArray())
        {
            Stop(index);
        }
    }
}
