using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex usb: the ADB interfaces Windows registered, the USB devices it could not read, and their
/// repairs. repair, enable-auto-repair and disable-auto-repair change the PC and need
/// administrator rights: the human CLI asks through the Windows prompt, machine mode needs an
/// elevated shell, and --dry-run says what would happen without either.
/// </summary>
public static class UsbCommands
{
    public static IReadOnlyList<string> Verbs { get; } = ["list", "status", "repair", "enable-auto-repair", "disable-auto-repair", "run-auto-repair"];

    public static bool IsDryRun(string[] args) => args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));

    private static string Verb(string[] positional) => positional.Length >= 2 ? positional[1].ToLowerInvariant() : "list";

    // ----- Human -----

    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var dryRun = IsDryRun(args);
        switch (Verb(Arguments.Positional(args)))
        {
            case "list":
            case "status":
                return List(context.Usb);
            case "repair":
                return await RepairAsync(context, dryRun).ConfigureAwait(false);
            case "enable-auto-repair":
                return await EnableAsync(context, dryRun).ConfigureAwait(false);
            case "disable-auto-repair":
                return await DisableAsync(context, dryRun).ConfigureAwait(false);
            case "run-auto-repair":
                context.Usb.AutoRepair.Run();
                Console.WriteLine("Asked Windows to run USB auto-repair. A phone it can recover appears within a few seconds.");
                return 0;
            default:
                throw new ArgumentException("usb expects " + string.Join(", ", Verbs) + ".");
        }
    }

    private static int List(UsbSystem usb)
    {
        var interfaces = usb.AdbInterfaces();
        Console.WriteLine("ADB interfaces:");
        if (interfaces.Count == 0)
        {
            Console.WriteLine("  Windows has no ADB interface registered. Plug the phone in with USB debugging turned on.");
        }

        foreach (var adbInterface in interfaces)
        {
            var state = adbInterface.Unreachable ? "PLUGGED IN, NOT REGISTERED FOR ADB" : adbInterface.Present ? "ok" : "not attached";
            Console.WriteLine($"  {state,-36} {adbInterface.InstanceId}  ({adbInterface.Description}, {adbInterface.Driver})");
        }

        var problems = usb.Problems();
        Console.WriteLine("USB devices Windows could not read:" + (problems.Count == 0 ? " none" : string.Empty));
        foreach (var problem in problems)
        {
            Console.WriteLine($"  {problem.InstanceId}  {problem.Describe()}");
        }

        Console.WriteLine("Auto-repair: " + usb.AutoRepair.Status().Describe());
        if (problems.Count > 0 || interfaces.Any(x => x.Unreachable))
        {
            Console.WriteLine("Run 'rex usb repair' to fix them (asks for administrator approval).");
        }

        return 0;
    }

    private static async Task<int> RepairAsync(CliContext context, bool dryRun)
    {
        var usb = context.Usb;
        var unreachable = usb.AdbInterfaces().Where(x => x.Unreachable).Select(x => x.InstanceId).ToArray();
        var plan = new UsbRepairPlan(usb.Problems());
        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing is changed.");
            Console.WriteLine(unreachable.Length == 0
                ? "No ADB interface needs registering."
                : $"Would register {unreachable.Length} ADB interface(s) for adb: {string.Join(", ", unreachable)}");
            foreach (var line in plan.Describe(usb.Pnputil))
            {
                Console.WriteLine(line);
            }

            return 0;
        }

        if (!usb.IsElevated())
        {
            return await ElevateAsync(context, "repair", "Repair finished. The phone should appear within a few seconds.", "The repair did not finish.").ConfigureAwait(false);
        }

        var registered = await usb.RegisterAdbInterfaces(usb.Runner, CancellationToken.None).ConfigureAwait(false);
        Report(context, registered.Count == 0
            ? "Nothing to register: every attached ADB interface is registered."
            : $"Registered {registered.Count} ADB interface(s): {string.Join(", ", registered)}");
        await UsbRepair.RunAsync(plan, usb, line => Report(context, line)).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> EnableAsync(CliContext context, bool dryRun)
    {
        var usb = context.Usb;
        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing is changed.");
            Console.WriteLine($"Would register the scheduled task {UsbAutoRepairTask.TaskPath}; Windows asks for administrator approval once.");
            Console.WriteLine("Windows runs it as SYSTEM, only when it is started, and it starts nothing but:");
            foreach (var arguments in UsbAutoRepairTask.Arguments)
            {
                Console.WriteLine($"  {UsbAutoRepairTask.Command(usb.SystemDirectory)} {arguments}");
            }

            Console.WriteLine("SYSTEM and Administrators own it. Anyone signed in at this PC may start it; nobody else may change it.");
            Console.WriteLine("Security descriptor: " + UsbAutoRepairTask.SecurityDescriptor);
            return 0;
        }

        if (!usb.IsElevated())
        {
            return await ElevateAsync(context, "enable-auto-repair",
                "USB auto-repair is set up. The app now fixes \"USB device not recognised\" without asking.",
                "USB auto-repair was not set up.").ConfigureAwait(false);
        }

        var status = usb.AutoRepair.Enable();
        Report(context, $"Registered {UsbAutoRepairTask.TaskPath}: {status.Describe()}.");
        return 0;
    }

    private static async Task<int> DisableAsync(CliContext context, bool dryRun)
    {
        var usb = context.Usb;
        var status = usb.AutoRepair.Status();
        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing is changed.");
            Console.WriteLine(status.State == UsbAutoRepairState.NotInstalled
                ? $"{UsbAutoRepairTask.TaskPath} is not set up, so there is nothing to delete."
                : $"Would delete the scheduled task {UsbAutoRepairTask.TaskPath}, which is {status.Describe()}; Windows asks for administrator approval once.");
            return 0;
        }

        if (status.State == UsbAutoRepairState.NotInstalled)
        {
            Console.WriteLine("USB auto-repair is not set up; there is nothing to remove.");
            return 0;
        }

        if (!usb.IsElevated())
        {
            return await ElevateAsync(context, "disable-auto-repair", "USB auto-repair is removed.", "USB auto-repair was not removed.").ConfigureAwait(false);
        }

        Report(context, usb.AutoRepair.Disable() ? $"Deleted {UsbAutoRepairTask.TaskPath}." : "USB auto-repair was not set up.");
        return 0;
    }

    /// <summary>Runs this same command again through the Windows prompt; the elevated copy does the work.</summary>
    private static async Task<int> ElevateAsync(CliContext context, string verb, string done, string failed)
    {
        try
        {
            var exitCode = await context.Usb.Elevate(Environment.ProcessPath!, ["usb", verb]).ConfigureAwait(false);
            Console.WriteLine(exitCode == 0 ? done : $"{failed} Details are in {context.Log.Path}.");
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Cancelled at the administrator prompt.");
        }
    }

    /// <summary>Elevated steps print and are logged, because the prompted copy runs in a window of its own.</summary>
    private static void Report(CliContext context, string line)
    {
        Console.WriteLine(line);
        context.Log.Info("usb: " + line);
    }

    // ----- Machine -----

    public static async Task<MachineResult> MachineAsync(CliContext context, string[] positional, bool dryRun)
    {
        var usb = context.Usb;
        switch (Verb(positional))
        {
            case "list":
                return MachineMode.Success("usb", Interfaces(usb.AdbInterfaces()));

            case "status":
                return MachineMode.Success("usb.status", new JsonObject
                {
                    ["interfaces"] = Interfaces(usb.AdbInterfaces()),
                    ["problems"] = Problems(usb.Problems()),
                    ["autoRepair"] = AutoRepair(usb.AutoRepair.Status()),
                });

            case "repair":
            {
                var plan = new UsbRepairPlan(usb.Problems());
                if (dryRun)
                {
                    return MachineMode.Success("usb", new JsonObject
                    {
                        ["dryRun"] = true,
                        ["register"] = Strings(usb.AdbInterfaces().Where(x => x.Unreachable).Select(x => x.InstanceId)),
                        ["problems"] = Problems(plan.Problems),
                        ["restart"] = Strings(plan.Restarts.Select(c => c.CommandLine(usb.Pnputil))),
                        ["fallback"] = Strings(UsbRepairPlan.Fallback(plan.Problems).Select(c => c.CommandLine(usb.Pnputil))),
                    });
                }

                RequireElevated(usb, "repair", "it restarts devices and edits HKLM device registrations");
                var repaired = await usb.RegisterAdbInterfaces(usb.Runner, CancellationToken.None).ConfigureAwait(false);
                var outcome = await UsbRepair.RunAsync(plan, usb, line => context.Log.Info("usb: " + line)).ConfigureAwait(false);
                return MachineMode.Success("usb", new JsonObject
                {
                    ["repaired"] = Strings(repaired),
                    ["restarted"] = Strings(outcome.Restarted),
                    ["removed"] = Strings(outcome.Removed),
                    ["stillFailing"] = Problems(outcome.StillFailing),
                    ["failures"] = Strings(outcome.Failures),
                });
            }

            case "enable-auto-repair":
                if (dryRun)
                {
                    return MachineMode.Success("usb.enable-auto-repair", new JsonObject
                    {
                        ["dryRun"] = true,
                        ["task"] = UsbAutoRepairTask.TaskPath,
                        ["runsAs"] = UsbAutoRepairTask.SystemSid,
                        ["command"] = UsbAutoRepairTask.Command(usb.SystemDirectory),
                        ["arguments"] = Strings(UsbAutoRepairTask.Arguments),
                        ["securityDescriptor"] = UsbAutoRepairTask.SecurityDescriptor,
                        ["xml"] = usb.AutoRepair.Xml,
                    });
                }

                RequireElevated(usb, "enable-auto-repair", "it registers a scheduled task that runs as SYSTEM");
                return MachineMode.Success("usb.enable-auto-repair", AutoRepair(usb.AutoRepair.Enable()));

            case "disable-auto-repair":
            {
                var status = usb.AutoRepair.Status();
                if (dryRun || status.State == UsbAutoRepairState.NotInstalled)
                {
                    return MachineMode.Success("usb.disable-auto-repair", new JsonObject
                    {
                        ["dryRun"] = dryRun,
                        ["task"] = UsbAutoRepairTask.TaskPath,
                        ["state"] = status.StateName,
                        ["removed"] = false,
                    });
                }

                RequireElevated(usb, "disable-auto-repair", "it deletes a scheduled task that runs as SYSTEM");
                return MachineMode.Success("usb.disable-auto-repair", new JsonObject
                {
                    ["dryRun"] = false,
                    ["task"] = UsbAutoRepairTask.TaskPath,
                    ["removed"] = usb.AutoRepair.Disable(),
                    ["state"] = usb.AutoRepair.Status().StateName,
                });
            }

            case "run-auto-repair":
                usb.AutoRepair.Run();
                return MachineMode.Success("usb.run-auto-repair", new JsonObject { ["task"] = UsbAutoRepairTask.TaskPath, ["started"] = true });

            default:
                throw new ArgumentException("usb expects " + string.Join(", ", Verbs) + ".");
        }
    }

    /// <summary>Machine mode never prompts, so what needs administrator rights needs an elevated shell.</summary>
    private static void RequireElevated(UsbSystem usb, string verb, string why)
    {
        if (!usb.IsElevated())
        {
            throw new InvalidOperationException($"usb {verb} needs an administrator shell in machine mode ({why}). Use --dry-run to see what it would do.");
        }
    }

    private static JsonArray Interfaces(IEnumerable<AdbInterface> interfaces) =>
        new(interfaces.Select(u => (JsonNode)new JsonObject
        {
            ["instanceId"] = u.InstanceId,
            ["description"] = u.Description,
            ["driver"] = u.Driver,
            ["present"] = u.Present,
            ["registered"] = u.Registered,
            ["unreachable"] = u.Unreachable,
        }).ToArray());

    private static JsonArray Problems(IEnumerable<UsbProblem> problems) =>
        new(problems.Select(p => (JsonNode)new JsonObject
        {
            ["instanceId"] = p.InstanceId,
            ["description"] = p.Node.Description,
            ["hardwareIds"] = Strings(p.Node.HardwareIds),
            ["problemCode"] = p.Node.ProblemCode,
            ["kind"] = p.Kind == UsbProblemKind.FailedEnumeration ? "failed-enumeration" : "phone-device",
            ["vendor"] = p.Vendor,
            ["autoRepairable"] = p.AutoRepairable,
        }).ToArray());

    private static JsonObject AutoRepair(UsbAutoRepairStatus status) => new()
    {
        ["task"] = UsbAutoRepairTask.TaskPath,
        ["state"] = status.StateName,
        ["lastRun"] = status.LastRun?.ToString("o", CultureInfo.InvariantCulture),
        ["lastResult"] = status.LastResult,
    };

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());
}
