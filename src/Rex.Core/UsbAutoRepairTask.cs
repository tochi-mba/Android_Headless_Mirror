using System.Xml;
using System.Xml.Linq;

namespace Rex.Core;

/// <summary>
/// The no-prompt USB repair: one scheduled task, registered once with the user's approval, that
/// Windows runs as SYSTEM whenever the app asks.
///
/// The installer is per-user, so the app's own executables can be replaced by anything running as
/// the user; a task that ran them with administrator rights would hand those rights to whatever
/// replaced them. This task therefore runs nothing but Windows' own pnputil.exe from System32,
/// with fixed arguments that only touch devices the USB hub already gave up on (the generic ids
/// of <see cref="UsbProblems.FailedEnumerationIds"/>): restart them, scan for hardware changes,
/// remove them, scan again. People signed in at the PC may read and start it; only SYSTEM and
/// Administrators may change or delete it.
/// </summary>
public static class UsbAutoRepairTask
{
    public const string Folder = "REX";
    public const string Name = "USB auto-repair";
    public const string TaskPath = @"\REX\USB auto-repair";

    /// <summary>LocalSystem, as a SID so it reads the same on every language of Windows.</summary>
    public const string SystemSid = "S-1-5-18";

    /// <summary>
    /// Owned by Administrators; SYSTEM and Administrators have full control; INTERACTIVE (anyone
    /// signed in at this PC, locally or over Remote Desktop) may read and start it (FRFX, the
    /// 0x1200a9 Windows' own on-demand tasks grant) and nothing more. Protected, so nothing is
    /// inherited from the folder above.
    /// </summary>
    public const string SecurityDescriptor = "O:BAG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;IU)";

    public const string Description =
        "Asks Windows to recover USB devices it could not read (\"USB device not recognised\"), " +
        "so Android Headless Mirror can see the phone again. Runs only pnputil.exe from System32 with fixed " +
        "arguments that restart and re-enumerate devices already in a failed state.";

    private static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    /// pnputil's arguments, one action each, in order. pnputil has no /problem filter for restart
    /// or remove, and a failed node's VID_0000 instance id is not one of its hardware ids, so the
    /// generic failure ids are what /deviceid can match. Restarting first gives the hub its own
    /// retry; the scan settles that before anything is removed; removing and scanning again is
    /// Device Manager's "Uninstall device" and "Scan for hardware changes".
    /// </summary>
    public static IReadOnlyList<string> Arguments { get; } =
    [
        .. UsbProblems.FailedEnumerationIds.Select(id => $"/restart-device /deviceid \"{id}\""),
        "/scan-devices",
        .. UsbProblems.FailedEnumerationIds.Select(id => $"/remove-device /deviceid \"{id}\""),
        "/scan-devices",
    ];

    public static string Command(string systemDirectory) => Path.Combine(systemDirectory, "pnputil.exe");

    /// <summary>The definition Task Scheduler is given: no triggers, on demand only, one instance at a time.</summary>
    public static string Xml(string systemDirectory)
    {
        var command = Command(systemDirectory);
        var task = new XElement(Schema + "Task", new XAttribute("version", "1.2"),
            new XElement(Schema + "RegistrationInfo",
                new XElement(Schema + "Author", "Android Headless Mirror"),
                new XElement(Schema + "Description", Description),
                new XElement(Schema + "URI", TaskPath),
                new XElement(Schema + "SecurityDescriptor", SecurityDescriptor)),
            new XElement(Schema + "Triggers"),
            new XElement(Schema + "Principals",
                new XElement(Schema + "Principal", new XAttribute("id", "Author"),
                    new XElement(Schema + "UserId", SystemSid),
                    new XElement(Schema + "RunLevel", "HighestAvailable"))),
            new XElement(Schema + "Settings",
                Setting("MultipleInstancesPolicy", "IgnoreNew"),
                Setting("DisallowStartIfOnBatteries", "false"),
                Setting("StopIfGoingOnBatteries", "false"),
                Setting("AllowHardTerminate", "true"),
                Setting("StartWhenAvailable", "false"),
                Setting("RunOnlyIfNetworkAvailable", "false"),
                new XElement(Schema + "IdleSettings", Setting("StopOnIdleEnd", "false"), Setting("RestartOnIdle", "false")),
                Setting("AllowStartOnDemand", "true"),
                Setting("Enabled", "true"),
                Setting("Hidden", "false"),
                Setting("RunOnlyIfIdle", "false"),
                Setting("WakeToRun", "false"),
                Setting("ExecutionTimeLimit", "PT5M"),
                Setting("Priority", "7")),
            new XElement(Schema + "Actions", new XAttribute("Context", "Author"),
                Arguments.Select(arguments => new XElement(Schema + "Exec",
                    new XElement(Schema + "Command", command),
                    new XElement(Schema + "Arguments", arguments),
                    new XElement(Schema + "WorkingDirectory", systemDirectory)))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;

        static XElement Setting(string name, string value) => new(Schema + name, value);
    }

    /// <summary>
    /// Whether a registered task is exactly this one where it matters: it runs as SYSTEM and its
    /// actions are these pnputil calls and nothing else. Anything else at the same path, an older
    /// definition or one someone edited, is never started.
    /// </summary>
    public static bool IsCurrent(string xml, string systemDirectory)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(xml).Root;
        }
        catch (XmlException)
        {
            return false;
        }

        if (root is null)
        {
            return false;
        }

        var ns = root.Name.Namespace;
        var user = root.Element(ns + "Principals")?.Element(ns + "Principal")?.Element(ns + "UserId")?.Value.Trim() ?? string.Empty;
        var runsAsSystem = user.Equals(SystemSid, StringComparison.OrdinalIgnoreCase) ||
                           user.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                           user.Equals(@"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase);
        var actions = root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
        return runsAsSystem &&
               actions.Length == Arguments.Count &&
               actions.Zip(Arguments).All(pair =>
                   pair.First.Name.LocalName == "Exec" &&
                   string.Equals(pair.First.Element(ns + "Command")?.Value.Trim(), Command(systemDirectory), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pair.First.Element(ns + "Arguments")?.Value.Trim(), pair.Second, StringComparison.Ordinal));
    }
}

/// <summary>A registered task as Task Scheduler reports it.</summary>
public sealed record ScheduledTaskInfo(string Xml, DateTime? LastRun, int? LastResult);

/// <summary>The few things the USB recovery asks of Task Scheduler.</summary>
public interface ITaskScheduler
{
    /// <summary>The task at this path, or null when there is none.</summary>
    /// <exception cref="UnauthorizedAccessException">A task is there, but this account may not read it.</exception>
    ScheduledTaskInfo? Find(string path);

    /// <summary>Creates the folder if needed (with the given security) and registers or replaces the task. Needs administrator rights.</summary>
    void Register(string folder, string name, string xml, string securityDescriptor);

    /// <summary>Deletes the task, and the folder once it is empty; false when there was no task. Needs administrator rights.</summary>
    bool Delete(string folder, string name);

    /// <summary>Starts the task now; whoever its security descriptor allows may.</summary>
    void Run(string path);
}

public enum UsbAutoRepairState
{
    NotInstalled,
    Installed,

    /// <summary>A task is at the path, but not this definition: another version's, or edited.</summary>
    Outdated,

    Unreadable,
}

public sealed record UsbAutoRepairStatus(UsbAutoRepairState State, DateTime? LastRun = null, int? LastResult = null)
{
    public bool Ready => State == UsbAutoRepairState.Installed;

    /// <summary>The state as the CLI's JSON and the app's status name it.</summary>
    public string StateName => State switch
    {
        UsbAutoRepairState.Installed => "installed",
        UsbAutoRepairState.Outdated => "outdated",
        UsbAutoRepairState.Unreadable => "unreadable",
        _ => "not-installed",
    };

    public string Describe() => State switch
    {
        UsbAutoRepairState.Installed => "on (Windows runs it as SYSTEM when the app asks" +
            (LastRun is { } run ? $"; last run {run:yyyy-MM-dd HH:mm}, result 0x{LastResult ?? 0:X})" : "; not run yet)"),
        UsbAutoRepairState.Outdated => "set up by another version ('rex usb enable-auto-repair' updates it)",
        UsbAutoRepairState.Unreadable => "present, but this account cannot read it",
        _ => "not set up ('rex usb enable-auto-repair' sets it up; Windows asks for administrator approval once)",
    };
}

/// <summary>Sets up, checks, starts and removes <see cref="UsbAutoRepairTask"/>.</summary>
public sealed class UsbAutoRepair(ITaskScheduler scheduler, string systemDirectory)
{
    public string Xml => UsbAutoRepairTask.Xml(systemDirectory);

    public UsbAutoRepairStatus Status()
    {
        try
        {
            var task = scheduler.Find(UsbAutoRepairTask.TaskPath);
            return task is null
                ? new UsbAutoRepairStatus(UsbAutoRepairState.NotInstalled)
                : new UsbAutoRepairStatus(
                    UsbAutoRepairTask.IsCurrent(task.Xml, systemDirectory) ? UsbAutoRepairState.Installed : UsbAutoRepairState.Outdated,
                    task.LastRun,
                    task.LastResult);
        }
        catch (UnauthorizedAccessException)
        {
            return new UsbAutoRepairStatus(UsbAutoRepairState.Unreadable);
        }
    }

    /// <summary>Registers the task, or replaces another version's, and checks it reads back as asked. Needs administrator rights.</summary>
    public UsbAutoRepairStatus Enable()
    {
        scheduler.Register(UsbAutoRepairTask.Folder, UsbAutoRepairTask.Name, Xml, UsbAutoRepairTask.SecurityDescriptor);
        var status = Status();
        return status.Ready
            ? status
            : throw new InvalidOperationException($"Windows registered {UsbAutoRepairTask.TaskPath}, but it reads back as {status.Describe()}.");
    }

    public bool Disable() => scheduler.Delete(UsbAutoRepairTask.Folder, UsbAutoRepairTask.Name);

    /// <summary>Starts the task, but only when it is exactly this definition; no administrator rights needed.</summary>
    public void Run()
    {
        var status = Status();
        if (!status.Ready)
        {
            throw new InvalidOperationException($"USB auto-repair is {status.Describe()}.");
        }

        scheduler.Run(UsbAutoRepairTask.TaskPath);
    }
}
