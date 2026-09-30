using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>
/// Watches for a phone Windows could not read over USB ("USB device not recognised") while no
/// phone is ready, and gets it repaired: it starts the auto-repair task when that is set up and
/// allowed, and otherwise offers the prompted repair in the notice bar. What to do and when is
/// <see cref="UsbRecoveryPolicy"/>'s; this feeds it, carries out what it decides and tells the
/// window. It keeps working with the window in the tray. UI thread only.
/// </summary>
public sealed class UsbDoctor : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly AppHost _host;
    private readonly UsbSystem _usb;
    private readonly UsbRecoveryPolicy _policy = new();
    private readonly DispatcherTimer _timer;
    private bool _checking;
    private bool _wasAutomatic;
    private string _logged = string.Empty;

    public UsbDoctor(AppHost host, UsbSystem usb)
    {
        _host = host;
        _usb = usb;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public UsbRecoveryDecision Decision { get; private set; } = UsbRecoveryDecision.Quiet;

    /// <summary>What Windows reports, or last reported while a repair has it out of sight for a moment.</summary>
    public IReadOnlyList<UsbProblem> Problems { get; private set; } = [];

    public UsbAutoRepairStatus AutoRepair { get; private set; } = new(UsbAutoRepairState.NotInstalled);

    /// <summary>The task is set up and allowed, so repairs happen without asking.</summary>
    public bool RepairsAutomatically => _host.Config.App.AutoRepairUsb && AutoRepair.Ready;

    /// <summary>A repair or the set-up is waiting on the administrator prompt.</summary>
    public bool Busy { get; private set; }

    /// <summary>Completed looks, so a test can wait for a few more of them rather than for time.</summary>
    public int Checks { get; private set; }

    public event Action? Changed;

    private bool PhoneReady => _host.Session.Devices.Any(d => d.IsReady);

    public void Start()
    {
        _host.Session.Changed += OnSessionChanged;
        _timer.Start();
    }

    /// <summary>A phone that turns up ends the episode at once, without waiting for the next look.</summary>
    private void OnSessionChanged()
    {
        if (PhoneReady && _policy.InEpisode)
        {
            Apply(_policy.Observe(DateTime.UtcNow, phoneReady: true, [], automatic: false), []);
        }
    }

    public async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            IReadOnlyList<UsbProblem> problems = [];
            if (!PhoneReady)
            {
                var known = AutoRepair;
                (problems, AutoRepair) = await Task.Run(() => Scan(known));
            }

            var automatic = RepairsAutomatically && problems.Any(p => p.AutoRepairable);
            var decision = _policy.Observe(DateTime.UtcNow, PhoneReady, problems, automatic);
            if (decision.Trigger)
            {
                await TriggerAsync(decision.Attempts);
            }

            Checks++;
            Apply(decision, problems);
        }
        finally
        {
            _checking = false;
        }
    }

    private (IReadOnlyList<UsbProblem> Problems, UsbAutoRepairStatus Status) Scan(UsbAutoRepairStatus known)
    {
        var problems = _usb.Problems();

        // Task Scheduler is only asked while there is something it could help with.
        return (problems, problems.Count > 0 ? ReadStatus() : known);
    }

    private UsbAutoRepairStatus ReadStatus()
    {
        try
        {
            return _usb.AutoRepair.Status();
        }
        catch (InvalidOperationException ex)
        {
            _host.Log.Warn("Could not read the USB auto-repair task: " + ex.Message);
            return new UsbAutoRepairStatus(UsbAutoRepairState.NotInstalled);
        }
    }

    private async Task TriggerAsync(int attempt)
    {
        _host.Log.Info($"Starting USB auto-repair, attempt {attempt} of {UsbRecoveryPolicy.AttemptsPerEpisode}.");
        try
        {
            await Task.Run(() => _usb.AutoRepair.Run());
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _host.Log.Warn("USB auto-repair did not start: " + ex.Message);
        }
    }

    // ----- What the notice's buttons do -----

    /// <summary>"Fix USB": the full repair through the administrator prompt, drivers included.</summary>
    public async Task<(bool Ok, string Message)> RepairAsync()
    {
        var result = await ElevateAsync(
            ["usb", "repair"],
            "USB repaired. The phone should appear in a moment; unplug it and plug it in again if it does not.",
            "The repair did not finish. Run 'rex usb repair' in an administrator terminal to see why.");
        _policy.RecordAttempt(DateTime.UtcNow);
        await CheckAsync();
        return result;
    }

    /// <summary>"Fix automatically from now on": sets the task up (one prompt) if it is not, and turns the setting on.</summary>
    public async Task<(bool Ok, string Message)> EnableAutoRepairAsync()
    {
        if (!AutoRepair.Ready)
        {
            var result = await ElevateAsync(
                ["usb", "enable-auto-repair"],
                string.Empty,
                "USB auto-repair was not set up. Run 'rex usb enable-auto-repair' in a terminal to see why.");
            if (!result.Ok)
            {
                return result;
            }
        }

        if (!_host.Config.App.AutoRepairUsb)
        {
            _host.UpdateConfig(c => c.App.AutoRepairUsb = true);
        }

        await CheckAsync();
        return (true, "USB auto-repair is on. The app fixes this by itself from now on.");
    }

    /// <summary>"Hide": nothing more is said until this spell ends; repairs carry on.</summary>
    public void Dismiss()
    {
        _policy.Dismiss();
        Apply(Decision with { Notice = UsbNotice.None, Trigger = false }, Problems);
    }

    private async Task<(bool Ok, string Message)> ElevateAsync(IReadOnlyList<string> arguments, string done, string failed)
    {
        if (Busy)
        {
            return (false, "A USB repair is already waiting for approval.");
        }

        Busy = true;
        Changed?.Invoke();
        try
        {
            var exitCode = await _usb.Elevate(Path.Combine(AppContext.BaseDirectory, "rex.exe"), arguments);
            _host.Log.Info($"rex {string.Join(' ', arguments)} ran with administrator approval and exited with {exitCode}.");
            return exitCode == 0 ? (true, done) : (false, failed);
        }
        catch (OperationCanceledException)
        {
            return (false, "Cancelled at the administrator prompt.");
        }
        catch (InvalidOperationException ex)
        {
            return (false, "Could not start the repair: " + ex.Message);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private void Apply(UsbRecoveryDecision decision, IReadOnlyList<UsbProblem> problems)
    {
        // A repair makes the node vanish for a moment; the notice keeps naming what it was.
        var shown = problems.Count > 0 || !_policy.InEpisode ? problems : Problems;
        Log(shown);
        var changed = decision.Notice != Decision.Notice || decision.Attempts != Decision.Attempts ||
                      RepairsAutomatically != _wasAutomatic ||
                      !shown.Select(p => p.InstanceId).SequenceEqual(Problems.Select(p => p.InstanceId));
        Decision = decision;
        Problems = shown;
        _wasAutomatic = RepairsAutomatically;
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void Log(IReadOnlyList<UsbProblem> problems)
    {
        var key = string.Join("|", problems.Select(p => p.InstanceId));
        if (key == _logged)
        {
            return;
        }

        _logged = key;
        _host.Log.Info(problems.Count == 0
            ? "Windows no longer reports a USB device it could not read."
            : "Windows reports a USB device it could not read: " + string.Join("; ", problems.Select(p => $"{p.InstanceId} ({p.Describe()})")));
    }

    /// <summary>For the pipe's status, so the tests can follow the notice and the repairs.</summary>
    public JsonObject Status() => new()
    {
        ["notice"] = Decision.Notice.ToString().ToLowerInvariant(),
        ["attempts"] = Decision.Attempts,
        ["checks"] = Checks,
        ["automatic"] = RepairsAutomatically,
        ["autoRepair"] = AutoRepair.StateName,
        ["busy"] = Busy,
        ["problems"] = new JsonArray(Problems.Select(p => (JsonNode)p.InstanceId).ToArray()),
    };

    public void Dispose()
    {
        _timer.Stop();
        _host.Session.Changed -= OnSessionChanged;
    }
}
