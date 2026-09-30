using System.Runtime.InteropServices;

namespace Rex.Core;

/// <summary>
/// Task Scheduler through its own COM API (Schedule.Service), late bound. Nothing is written to a
/// file on the way, so nobody can swap the definition between writing and registering it.
/// Registering and deleting need administrator rights; <see cref="Find"/> and <see cref="Run"/>
/// work for anyone the task's security descriptor allows. Tests never reach this class: the
/// seam in <see cref="UsbSystem.FromEnvironment"/> gives them <see cref="RecordingTaskScheduler"/>.
/// </summary>
public sealed class WindowsTaskScheduler : ITaskScheduler
{
    private const int CreateOrUpdate = 6;        // TASK_CREATE_OR_UPDATE
    private const int ServiceAccountLogon = 5;   // TASK_LOGON_SERVICE_ACCOUNT
    private const int IncludeHidden = 1;         // TASK_ENUM_HIDDEN
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);
    private const int AccessDenied = unchecked((int)0x80070005);

    public ScheduledTaskInfo? Find(string path) => Guard<ScheduledTaskInfo?>(() =>
    {
        var (folder, name) = Split(path);
        dynamic service = Connect();
        dynamic task;
        try
        {
            task = service.GetFolder(folder).GetTask(name);
        }
        catch (COMException ex) when (IsMissing(ex))
        {
            return null;
        }

        string xml = task.Xml;
        DateTime lastRun = task.LastRunTime;
        int lastResult = task.LastTaskResult;

        // A task that never ran reports the COM epoch, 1899.
        return new ScheduledTaskInfo(xml, lastRun.Year < 2000 ? null : lastRun, lastResult);
    });

    public void Register(string folder, string name, string xml, string securityDescriptor) => Guard(() =>
    {
        dynamic service = Connect();
        dynamic target;
        try
        {
            target = service.GetFolder(@"\" + folder);

            // A folder that was already there gets the same owner and rules as a new one, so
            // nobody but administrators can put anything next to the task or replace it.
            target.SetSecurityDescriptor(securityDescriptor, 0);
        }
        catch (COMException ex) when (IsMissing(ex))
        {
            target = service.GetFolder(@"\").CreateFolder(folder, securityDescriptor);
        }

        // No account is passed: the definition's own principal (S-1-5-18) is used, which reads
        // the same whatever language Windows speaks.
        target.RegisterTask(name, xml, CreateOrUpdate, null, null, ServiceAccountLogon, securityDescriptor);
        return true;
    });

    public bool Delete(string folder, string name) => Guard(() =>
    {
        dynamic service = Connect();
        dynamic target;
        try
        {
            target = service.GetFolder(@"\" + folder);
        }
        catch (COMException ex) when (IsMissing(ex))
        {
            return false;
        }

        var deleted = true;
        try
        {
            target.DeleteTask(name, 0);
        }
        catch (COMException ex) when (IsMissing(ex))
        {
            deleted = false;
        }

        // The folder goes too once nothing else lives in it.
        int tasks = target.GetTasks(IncludeHidden).Count;
        int folders = target.GetFolders(0).Count;
        if (tasks == 0 && folders == 0)
        {
            service.GetFolder(@"\").DeleteFolder(folder, 0);
        }

        return deleted;
    });

    public void Run(string path) => Guard(() =>
    {
        var (folder, name) = Split(path);
        dynamic service = Connect();
        service.GetFolder(folder).GetTask(name).Run(null);
        return true;
    });

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    /// <summary>"\REX\USB auto-repair" is the task "USB auto-repair" in the folder "\REX".</summary>
    private static (string Folder, string Name) Split(string path)
    {
        var at = path.LastIndexOf('\\');
        return (at <= 0 ? @"\" : path[..at], path[(at + 1)..]);
    }

    private static bool IsMissing(COMException ex) => ex.HResult is FileNotFound or PathNotFound;

    /// <summary>Task Scheduler's errors, in the exceptions the CLI and the app already report.</summary>
    private static T Guard<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (COMException ex) when (ex.HResult == AccessDenied)
        {
            throw new UnauthorizedAccessException("Task Scheduler refused access: " + ex.Message, ex);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Task Scheduler reported 0x{ex.HResult:X8}: {ex.Message}", ex);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new InvalidOperationException("Task Scheduler did not answer as expected: " + ex.Message, ex);
        }
    }
}

/// <summary>
/// Stands in for Task Scheduler and the administrator prompt when
/// <see cref="UsbSystem.FakeRepairLogVariable"/> names a file: a registration is kept beside the
/// log, and every call is appended to it, so the end-to-end tests can watch the app work the
/// repair with no real task and no prompt. It only ever writes those two files.
/// </summary>
public sealed class RecordingTaskScheduler(string log) : ITaskScheduler
{
    private static readonly object Gate = new();

    /// <summary>Where a registered definition is kept.</summary>
    public string Store => log + ".task.xml";

    public ScheduledTaskInfo? Find(string path)
    {
        lock (Gate)
        {
            return File.Exists(Store) ? new ScheduledTaskInfo(File.ReadAllText(Store), null, null) : null;
        }
    }

    public void Register(string folder, string name, string xml, string securityDescriptor)
    {
        lock (Gate)
        {
            File.WriteAllText(Store, xml);
        }

        Record($@"register \{folder}\{name} {securityDescriptor}");
    }

    public bool Delete(string folder, string name)
    {
        bool existed;
        lock (Gate)
        {
            existed = File.Exists(Store);
            File.Delete(Store);
        }

        Record($@"delete \{folder}\{name}");
        return existed;
    }

    public void Run(string path) => Record("run " + path);

    /// <summary>
    /// Plays the elevated rex.exe the app would start: records the command, and for the two that
    /// change the task, does to this stand-in what the real one does to Task Scheduler.
    /// </summary>
    public Task<int> ElevateAsync(IReadOnlyList<string> arguments, string systemDirectory)
    {
        var command = string.Join(' ', arguments);
        Record("elevate " + command);
        var repair = new UsbAutoRepair(this, systemDirectory);
        if (command == "usb enable-auto-repair")
        {
            repair.Enable();
        }
        else if (command == "usb disable-auto-repair")
        {
            repair.Disable();
        }

        return Task.FromResult(0);
    }

    private void Record(string line)
    {
        lock (Gate)
        {
            // The test reads the log while the app writes it, so neither may lock the other out.
            using var stream = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
        }
    }
}
