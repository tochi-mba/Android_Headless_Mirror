using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex.exe: the scriptable side of Android Headless Mirror. Plain commands print for humans;
/// <c>agent</c>, <c>--json</c> or <c>--plain</c> switch to the one-JSON-document machine mode.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ProcessRunner.PreventStandardHandleInheritance();
        var machineArgs = MachineMode.ExtractArguments(args);
        AppPaths paths;
        try
        {
            paths = AppPaths.Discover();
        }
        catch (InvalidOperationException ex)
        {
            if (machineArgs is not null)
            {
                Console.Out.WriteLine(MachineMode.FailureJson("startup", ex));
            }
            else
            {
                Console.Error.WriteLine(ex.Message);
            }

            return 2;
        }

        var context = new CliContext(paths);
        if (machineArgs is not null)
        {
            var result = await MachineMode.RunAsync(machineArgs, context).ConfigureAwait(false);
            Console.Out.WriteLine(result.Json);
            return result.ExitCode;
        }

        try
        {
            return await Commands.RunAsync(args, context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException or FormatException
                                   or System.Net.Http.HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }
}

/// <summary>Everything a command needs; built once per process.</summary>
public sealed class CliContext
{
    public CliContext(AppPaths paths)
    {
        Paths = paths;
        Config = new ConfigStore(paths.Config);
        Runner = new ProcessRunner();
        Log = new RexLog(paths.LogFile, ConfigFile.Load(paths.Config).Logging);
        Ipc = new IpcClient();
    }

    public AppPaths Paths { get; }
    public ConfigStore Config { get; }
    public IProcessRunner Runner { get; }
    public RexLog Log { get; }
    public IpcClient Ipc { get; }

    public AdbClient? Adb()
    {
        var tools = ToolLocator.Find(Paths);
        return tools is null ? null : new AdbClient(tools.Adb, Runner);
    }

    public AdbClient RequireAdb() =>
        Adb() ?? throw new InvalidOperationException("scrcpy/adb are not installed. Run 'rex setup' or open the app.");

    /// <summary>The serial to act on: explicit, else the single ready phone. Never guesses between several.</summary>
    public async Task<string> ResolveSerialAsync(string? requested, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        var ready = (await RequireAdb().ListDevicesAsync(cancellationToken).ConfigureAwait(false)).Where(d => d.IsReady).ToArray();
        return ready.Length switch
        {
            1 => ready[0].Serial,
            0 => throw new InvalidOperationException("No authorized Android phone is connected."),
            _ => throw new InvalidOperationException("Several phones are connected. Pass --serial <SERIAL>."),
        };
    }

    public string AppExecutable => Path.Combine(AppContext.BaseDirectory, "RexMirror.exe");
}

public static class Arguments
{
    public static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Positional arguments: everything that is not an option or an option value.</summary>
    public static string[] Positional(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[i] is "--serial" or "--filter" && i + 1 < args.Length)
                {
                    i++;
                }

                continue;
            }

            result.Add(args[i]);
        }

        return result.ToArray();
    }

    public static void Require(string[] positional, int count, string usage)
    {
        if (positional.Length < count)
        {
            throw new ArgumentException("Usage: " + usage);
        }
    }
}
