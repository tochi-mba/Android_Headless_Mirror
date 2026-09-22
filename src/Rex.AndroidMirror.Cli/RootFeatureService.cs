using System.Text.RegularExpressions;

namespace Rex.AndroidMirror.Cli;

public sealed partial class RootFeatureService
{
    private readonly RootManager _root;
    private readonly RootPolicy _policy;

    public RootFeatureService(
        RootManager root,
        ConfigStore config)
    {
        _root = root;
        _policy = new RootPolicy(config);
    }

    public async Task<RootFeatureResult> DiagnosticsAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var commands = new[]
        {
            ("identity", Command("root.diagnostics.identity", "id")),
            ("selinux", Command("root.diagnostics.selinux", "getenforce")),
            ("kernel", Command("root.diagnostics.kernel", "uname", "-a")),
            ("mounts", Command("root.diagnostics.mounts", "cat", "/proc/mounts")),
            ("memory", Command("root.diagnostics.memory", "cat", "/proc/meminfo")),
            ("kernelCommandLine", Command("root.diagnostics.cmdline", "cat", "/proc/cmdline")),
            ("androidRelease", Command("root.diagnostics.release", "getprop", "ro.build.version.release")),
            ("securityPatch", Command("root.diagnostics.patch", "getprop", "ro.build.version.security_patch"))
        };

        return await CollectAsync(
            "root.diagnostics",
            serial,
            commands,
            cancellationToken);
    }

    public async Task<RootFeatureResult> ListFilesAsync(
        string serial,
        string path,
        CancellationToken cancellationToken = default)
    {
        path = ValidatePath(path, allowCritical: false);
        return await CollectAsync(
            "root.files.list",
            serial,
            new[]
            {
                ("listing", Command("root.files.list", "ls", "-laZ", path))
            },
            cancellationToken);
    }

    public async Task<RootFeatureResult> StatFileAsync(
        string serial,
        string path,
        CancellationToken cancellationToken = default)
    {
        path = ValidatePath(path, allowCritical: false);
        return await CollectAsync(
            "root.files.stat",
            serial,
            new[]
            {
                ("stat", Command(
                    "root.files.stat",
                    "stat",
                    "-c",
                    "%n|%s|%a|%u|%g|%F",
                    path))
            },
            cancellationToken);
    }

    public async Task<RootFeatureResult> ReadFileAsync(
        string serial,
        string path,
        CancellationToken cancellationToken = default)
    {
        path = ValidatePath(path, allowCritical: false);
        return await CollectAsync(
            "root.files.read",
            serial,
            new[]
            {
                ("content", new PrivilegedCommand(
                    "root.files.read",
                    "head",
                    new[] { "-c", "262144", path },
                    PrivilegeRisk.ReadOnly,
                    MaxOutputCharacters: 262144))
            },
            cancellationToken);
    }

    public async Task<RootFeatureResult> ProcessesAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            "root.processes",
            serial,
            new[]
            {
                ("processes", Command("root.processes", "ps", "-A"))
            },
            cancellationToken);

    public async Task<RootFeatureResult> ProcessAsync(
        string serial,
        int pid,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
            throw new ArgumentOutOfRangeException(nameof(pid), "PID must be positive.");

        var processRoot = $"/proc/{pid}";
        return await CollectAsync(
            "root.process",
            serial,
            new[]
            {
                ("status", Command("root.process.status", "cat", $"{processRoot}/status")),
                ("cmdline", Command("root.process.cmdline", "cat", $"{processRoot}/cmdline")),
                ("limits", Command("root.process.limits", "cat", $"{processRoot}/limits"))
            },
            cancellationToken);
    }

    public async Task<RootFeatureResult> AppAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        packageName = ValidatePackage(packageName);
        var dataPath = $"/data/user/0/{packageName}";

        return await CollectAsync(
            "root.apps.inspect",
            serial,
            new[]
            {
                ("apk", Command("root.apps.apk", "pm", "path", packageName)),
                ("package", Command("root.apps.package", "dumpsys", "package", packageName)),
                ("privateData", Command("root.apps.data", "ls", "-laZ", dataPath)),
                ("privateDataSize", Command("root.apps.size", "du", "-sk", dataPath))
            },
            cancellationToken);
    }

    public async Task<RootFeatureResult> HardwareAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            "root.hardware",
            serial,
            new[]
            {
                ("cpu", Command("root.hardware.cpu", "cat", "/proc/cpuinfo")),
                ("memory", Command("root.hardware.memory", "cat", "/proc/meminfo")),
                ("uptime", Command("root.hardware.uptime", "cat", "/proc/uptime")),
                ("thermalZones", Command("root.hardware.thermal", "ls", "-la", "/sys/class/thermal")),
                ("powerSupply", Command("root.hardware.power", "ls", "-la", "/sys/class/power_supply"))
            },
            cancellationToken);

    public async Task<RootFeatureResult> NetworkAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            "root.network",
            serial,
            new[]
            {
                ("interfaces", Command("root.network.interfaces", "ip", "addr")),
                ("routes", Command("root.network.routes", "ip", "route")),
                ("tcp", Command("root.network.tcp", "cat", "/proc/net/tcp")),
                ("tcp6", Command("root.network.tcp6", "cat", "/proc/net/tcp6"))
            },
            cancellationToken);

    public async Task<RootFeatureResult> KernelLogsAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            "root.logs.kernel",
            serial,
            new[]
            {
                ("kernel", new PrivilegedCommand(
                    "root.logs.kernel",
                    "sh",
                    new[] { "-c", "dmesg | tail -n 500" },
                    PrivilegeRisk.ReadOnly,
                    MaxOutputCharacters: 262144))
            },
            cancellationToken);

    public async Task<RootFeatureResult> PropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            "root.properties",
            serial,
            new[]
            {
                ("properties", Command("root.properties", "getprop"))
            },
            cancellationToken);

    private async Task<RootFeatureResult> CollectAsync(
        string operation,
        string serial,
        IEnumerable<(string Name, PrivilegedCommand Command)> commands,
        CancellationToken cancellationToken)
    {
        _policy.EnsureAllowed(PrivilegeRisk.ReadOnly);

        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, command) in commands)
        {
            var result = await _root.ExecuteVerifiedAsync(
                serial,
                command,
                cancellationToken);

            sections[name] = result.Ok
                ? result.StdOut.TrimEnd()
                : $"[unavailable] {(string.IsNullOrWhiteSpace(result.StdErr) ? $"exit {result.ExitCode}" : result.StdErr.Trim())}";
        }

        return new RootFeatureResult(
            operation,
            serial,
            PrivilegeRisk.ReadOnly,
            sections);
    }

    private static PrivilegedCommand Command(
        string id,
        string executable,
        params string[] arguments) =>
        new(id, executable, arguments, PrivilegeRisk.ReadOnly);

    public static string ValidatePath(string path, bool allowCritical)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/', StringComparison.Ordinal))
            throw new ArgumentException("Privileged file paths must be absolute Android paths.");

        if (path.Length > 4096 || path.Any(char.IsControl))
            throw new ArgumentException("The Android path is invalid.");

        var normalized = NormalizeAndroidPath(path);
        if (!allowCritical &&
            CriticalPathPrefixes.Any(prefix =>
                normalized.Equals(prefix, StringComparison.Ordinal) ||
                normalized.StartsWith(prefix + "/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"REX Root v1 blocks raw reads from device-critical path '{normalized}'.");
        }

        return normalized;
    }

    private static string NormalizeAndroidPath(string path)
    {
        var segments = new List<string>();

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new ArgumentException(
                        "The Android path cannot traverse above the filesystem root.");

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0
            ? "/"
            : "/" + string.Join('/', segments);
    }

    public static string ValidatePackage(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName) ||
            !PackageRegex().IsMatch(packageName))
            throw new ArgumentException("Android package name is invalid.");

        return packageName;
    }

    private static readonly string[] CriticalPathPrefixes =
    {
        "/dev/block",
        "/dev/mem",
        "/dev/kmem",
        "/proc/kcore"
    };

    [GeneratedRegex(@"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+$")]
    private static partial Regex PackageRegex();
}
