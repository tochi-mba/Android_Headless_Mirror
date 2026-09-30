using System.ComponentModel;
using System.Diagnostics;

namespace Rex.Core;

/// <summary>
/// Everything the USB recovery touches on the PC, in one place so the CLI, the app and the tests
/// agree on it: the device scan, Task Scheduler, pnputil, the ADB interface registry, the
/// administrator prompt and the clock. Every part is required, so a test cannot reach the real
/// PC by forgetting one.
/// </summary>
public sealed class UsbSystem
{
    /// <summary>
    /// A log file the end-to-end tests set instead of Task Scheduler and the administrator prompt:
    /// task registrations, runs and elevated launches are written there and never performed.
    /// </summary>
    public const string FakeRepairLogVariable = "REX_FAKE_USB_REPAIR_LOG";

    public required IUsbDeviceSource Devices { get; init; }
    public required ITaskScheduler Scheduler { get; init; }
    public required IProcessRunner Runner { get; init; }
    public required Func<bool> IsElevated { get; init; }

    /// <summary>
    /// Runs a program through the Windows administrator prompt and returns its exit code. Throws
    /// <see cref="OperationCanceledException"/> when the prompt is declined.
    /// </summary>
    public required Func<string, IReadOnlyList<string>, Task<int>> Elevate { get; init; }

    /// <summary>Every ADB interface Windows has registered (see <see cref="UsbAdbInterfaces.Scan"/>).</summary>
    public required Func<IReadOnlyList<AdbInterface>> AdbInterfaces { get; init; }

    /// <summary>Registers the unreachable ADB interfaces for adb (see <see cref="UsbAdbInterfaces.RepairAsync"/>). Needs administrator rights.</summary>
    public required Func<IProcessRunner, CancellationToken, Task<IReadOnlyList<string>>> RegisterAdbInterfaces { get; init; }
    public required string SystemDirectory { get; init; }
    public required Func<TimeSpan, CancellationToken, Task> Delay { get; init; }

    public string Pnputil => Path.Combine(SystemDirectory, "pnputil.exe");

    public UsbAutoRepair AutoRepair => new(Scheduler, SystemDirectory);

    /// <summary>The phones Windows could not read, as of now.</summary>
    public IReadOnlyList<UsbProblem> Problems() => UsbProblems.Classify(Devices.ProblemNodes());

    /// <summary>The real PC, unless the end-to-end tests' seams are set.</summary>
    public static UsbSystem FromEnvironment(IProcessRunner runner)
    {
        var systemDirectory = Environment.SystemDirectory;
        if (Environment.GetEnvironmentVariable(FakeRepairLogVariable) is { Length: > 0 } log)
        {
            var recorder = new RecordingTaskScheduler(log);
            return new UsbSystem
            {
                Devices = UsbDeviceSource.FromEnvironment(),
                Scheduler = recorder,
                Runner = runner,
                IsElevated = () => false,
                Elevate = (_, arguments) => recorder.ElevateAsync(arguments, systemDirectory),
                AdbInterfaces = () => [],
                RegisterAdbInterfaces = (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
                SystemDirectory = systemDirectory,
                Delay = Task.Delay,
            };
        }

        return new UsbSystem
        {
            Devices = UsbDeviceSource.FromEnvironment(),
            Scheduler = new WindowsTaskScheduler(),
            Runner = runner,
            IsElevated = () => UsbAdbInterfaces.IsElevated,
            Elevate = Elevation.RunAsync,
            AdbInterfaces = UsbAdbInterfaces.Scan,
            RegisterAdbInterfaces = UsbAdbInterfaces.RepairAsync,
            SystemDirectory = systemDirectory,
            Delay = Task.Delay,
        };
    }
}

/// <summary>The Windows administrator prompt, for the commands a person asked for and nothing else.</summary>
public static class Elevation
{
    /// <summary>ERROR_CANCELLED: the prompt was declined.</summary>
    private const int Declined = 1223;

    public static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException($"{Path.GetFileName(executable)} was not found at {executable}.");
        }

        var start = new ProcessStartInfo(executable, string.Join(' ', arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a)))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Windows did not start {Path.GetFileName(executable)}.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Declined)
        {
            throw new OperationCanceledException("The administrator prompt was declined.", ex);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Windows could not ask for administrator approval: " + ex.Message, ex);
        }
    }
}
