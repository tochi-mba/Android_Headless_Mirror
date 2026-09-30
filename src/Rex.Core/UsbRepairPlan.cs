namespace Rex.Core;

/// <summary>One pnputil call and why it is made.</summary>
public sealed record UsbRepairCommand(string Purpose, IReadOnlyList<string> Arguments)
{
    /// <summary>The call as it would be typed, for the dry run and the log.</summary>
    public string CommandLine(string pnputil) =>
        string.Join(' ', Arguments.Select(argument => argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('&', StringComparison.Ordinal) ? $"\"{argument}\"" : argument).Prepend(pnputil));
}

/// <summary>
/// What the prompted repair does to the phones Windows could not read: restart each failed node,
/// and only when Windows still reports it a moment later, remove it and scan for hardware changes
/// (Device Manager's "Uninstall device" then "Scan for hardware changes"). Nodes are named by
/// their exact instance id, read by the elevated process itself. Pure: nothing runs here.
/// </summary>
public sealed record UsbRepairPlan(IReadOnlyList<UsbProblem> Problems)
{
    /// <summary>How long a restarted device gets to come back before the fallback is tried.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    public static UsbRepairCommand ScanForHardwareChanges { get; } = new("Scan for hardware changes", ["/scan-devices"]);

    public IReadOnlyList<UsbRepairCommand> Restarts =>
        Problems.Select(problem => new UsbRepairCommand("Restart " + Name(problem), ["/restart-device", problem.InstanceId])).ToArray();

    /// <summary>Remove whatever is still failing, then have Windows enumerate it from scratch.</summary>
    public static IReadOnlyList<UsbRepairCommand> Fallback(IEnumerable<UsbProblem> stillFailing) =>
    [
        .. stillFailing.Select(problem => new UsbRepairCommand("Remove " + Name(problem), ["/remove-device", problem.InstanceId])),
        ScanForHardwareChanges,
    ];

    /// <summary>The dry run: every call the repair would make, in order, and when.</summary>
    public IReadOnlyList<string> Describe(string pnputil)
    {
        if (Problems.Count == 0)
        {
            return ["No USB device is in a failed state, so nothing would be restarted."];
        }

        return
        [
            "USB devices Windows could not read:",
            .. Problems.Select(problem => $"  {problem.InstanceId} ({problem.Describe()})"),
            "Would run, as administrator:",
            .. Restarts.Select(command => "  " + command.CommandLine(pnputil)),
            $"Then, for any of them Windows still reports {Settle.TotalSeconds:0} seconds later:",
            .. Fallback(Problems).Select(command => "  " + command.CommandLine(pnputil)),
        ];
    }

    private static string Name(UsbProblem problem) =>
        problem.Node.Description.Length > 0 ? $"{problem.Node.Description} ({problem.InstanceId})" : problem.InstanceId;
}

/// <summary>What the repair did, and what Windows still reports afterwards.</summary>
public sealed record UsbRepairOutcome(
    IReadOnlyList<string> Restarted,
    IReadOnlyList<string> Removed,
    IReadOnlyList<UsbProblem> StillFailing,
    IReadOnlyList<string> Failures);

/// <summary>Carries out a <see cref="UsbRepairPlan"/>. Needs administrator rights; logs every step.</summary>
public static class UsbRepair
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    public static async Task<UsbRepairOutcome> RunAsync(UsbRepairPlan plan, UsbSystem usb, Action<string> log, CancellationToken cancellationToken = default)
    {
        var restarted = new List<string>();
        var removed = new List<string>();
        var failures = new List<string>();
        if (plan.Problems.Count == 0)
        {
            log("No USB device is in a failed state.");
            return new UsbRepairOutcome(restarted, removed, [], failures);
        }

        foreach (var (command, problem) in plan.Restarts.Zip(plan.Problems))
        {
            if (await RunAsync(command, usb, log, failures, cancellationToken).ConfigureAwait(false))
            {
                restarted.Add(problem.InstanceId);
            }
        }

        await usb.Delay(UsbRepairPlan.Settle, cancellationToken).ConfigureAwait(false);
        var still = StillFailing(plan.Problems, usb.Problems());
        if (still.Count > 0)
        {
            log($"{still.Count} device(s) still failing after the restart; removing them and scanning for hardware changes.");
            foreach (var command in UsbRepairPlan.Fallback(still))
            {
                if (await RunAsync(command, usb, log, failures, cancellationToken).ConfigureAwait(false) && command.Arguments[0] == "/remove-device")
                {
                    removed.Add(command.Arguments[1]);
                }
            }

            await usb.Delay(UsbRepairPlan.Settle, cancellationToken).ConfigureAwait(false);
        }

        var after = usb.Problems();
        log(after.Count == 0
            ? "Windows no longer reports a USB device it could not read."
            : "Windows still reports: " + string.Join("; ", after.Select(problem => problem.Describe())));
        return new UsbRepairOutcome(restarted, removed, after, failures);
    }

    private static IReadOnlyList<UsbProblem> StillFailing(IReadOnlyList<UsbProblem> planned, IReadOnlyList<UsbProblem> now) =>
        planned.Where(problem => now.Any(current => string.Equals(current.InstanceId, problem.InstanceId, StringComparison.OrdinalIgnoreCase))).ToArray();

    /// <summary>
    /// One pnputil call. A device that refuses to restart is a result to report, not a reason to
    /// stop: the fallback still gets its turn. Only a pnputil that cannot be run at all throws.
    /// </summary>
    private static async Task<bool> RunAsync(UsbRepairCommand command, UsbSystem usb, Action<string> log, List<string> failures, CancellationToken cancellationToken)
    {
        log($"{command.Purpose}: {command.CommandLine(usb.Pnputil)}");
        var result = await usb.Runner.RunAsync(usb.Pnputil, command.Arguments, CommandTimeout, cancellationToken).ConfigureAwait(false);
        var said = LastLine(result.StdOut);
        log(result.Ok ? "  done" + (said.Length > 0 ? ": " + said : ".") : "  failed: " + (said.Length > 0 ? said : result.FailureText));
        if (!result.Ok)
        {
            failures.Add($"{command.CommandLine(usb.Pnputil)}: {(said.Length > 0 ? said : result.FailureText)}");
        }

        return result.Ok;
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
}
