using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.AndroidMirror.Cli;

public sealed partial class RootManager
{
    private readonly IBridgeClient _bridge;
    private readonly IAndroidShellRunner _shell;
    private readonly RootPolicy _policy;
    private readonly RootStateStore _state;

    public RootManager(
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config,
        IAndroidShellRunner? shell = null,
        RootStateStore? state = null)
    {
        _bridge = bridge;
        _policy = new RootPolicy(config);
        _shell = shell ?? new AndroidShellRunner(runner, paths.Root, _policy);
        _state = state ?? new RootStateStore(paths.RootState);
    }

    public async Task<RootStatus> ProbePassiveAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.Enabled)
        {
            return EmptyStatus(
                serial,
                RootAccessState.Unavailable,
                "Privileged Android support is disabled in REX config.");
        }

        var (adbPath, device) = await GetAdbContextAsync(serial, cancellationToken);
        if (device.State != "device")
            throw new InvalidOperationException(
                $"Android device '{serial}' is not authorized for ADB.");

        var adbUid = await ReadIntAsync(
            adbPath,
            serial,
            Command("root.probe.adb-uid", "id", "-u"),
            cancellationToken);

        var bootId = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.probe.boot-id", "cat", "/proc/sys/kernel/random/boot_id"),
            cancellationToken);

        var selinux = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.probe.selinux", "getenforce"),
            cancellationToken);

        var suPath = await ReadTextAsync(
            adbPath,
            serial,
            Command(
                "root.probe.su-path",
                "sh",
                "-c",
                "command -v su 2>/dev/null || true"),
            cancellationToken);

        var suVisible = !string.IsNullOrWhiteSpace(suPath);
        var cached = _state.Get(serial, bootId);

        if (adbUid == 0)
        {
            if (cached is not null && cached.State == RootAccessState.AdbdRoot)
                return FromCache(cached, adbUid, suVisible, suPath, selinux);

            return new RootStatus(
                serial,
                RootAccessState.AdbdRoot,
                RootProvider.None,
                string.Empty,
                adbUid,
                0,
                suVisible,
                suPath,
                bootId,
                selinux,
                null,
                Array.Empty<RootCapability>(),
                DateTimeOffset.UtcNow,
                false,
                "ADB daemon is already running as UID 0. Run 'rex root request' to verify privileged capabilities for this boot.");
        }

        if (cached is not null &&
            cached.State is RootAccessState.Granted or RootAccessState.GrantedRestricted)
        {
            return FromCache(cached, adbUid, suVisible, suPath, selinux);
        }

        return new RootStatus(
            serial,
            suVisible ? RootAccessState.SuDetected : RootAccessState.Unavailable,
            RootProvider.None,
            string.Empty,
            adbUid,
            null,
            suVisible,
            suPath,
            bootId,
            selinux,
            null,
            Array.Empty<RootCapability>(),
            DateTimeOffset.UtcNow,
            false,
            suVisible
                ? "A usable su command is visible. Root authorization has not been verified for this Android boot."
                : "No usable root route is currently visible to the ADB shell.");
    }

    public async Task<RootStatus> RequestAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        _policy.EnsureEnabled();

        var passive = await ProbePassiveAsync(serial, cancellationToken);
        var (adbPath, _) = await GetAdbContextAsync(serial, cancellationToken);

        RootExecutionMode mode;
        int? effectiveUid;

        if (passive.AdbUid == 0)
        {
            mode = RootExecutionMode.AdbdRoot;
            effectiveUid = 0;
        }
        else
        {
            if (!passive.SuVisible)
                return passive with
                {
                    State = RootAccessState.Unavailable,
                    Message = "No su command is visible to the ADB shell, so REX cannot request root access."
                };

            mode = RootExecutionMode.Su;
            var request = await _shell.RunAsync(
                adbPath,
                serial,
                new PrivilegedCommand(
                    "root.request",
                    "id",
                    new[] { "-u" },
                    PrivilegeRisk.ReadOnly,
                    TimeSpan.FromSeconds(_policy.RequestTimeoutSeconds),
                    4096),
                RootExecutionMode.Su,
                cancellationToken);

            if (request.TimedOut)
            {
                return passive with
                {
                    State = RootAccessState.AuthorizationPending,
                    Message = "Root authorization did not complete before the timeout. Check the phone for a superuser prompt, then retry."
                };
            }

            if (!request.Ok)
            {
                return passive with
                {
                    State = RootAccessState.Denied,
                    Message = DenialMessage(request)
                };
            }

            effectiveUid = ParseInt(request.StdOut);
            if (effectiveUid is null)
            {
                return passive with
                {
                    State = RootAccessState.Denied,
                    Message = "The su command returned success but did not report a numeric effective UID."
                };
            }
        }

        var profile = await ProbePrivilegeProfileAsync(
            adbPath,
            serial,
            mode,
            effectiveUid,
            cancellationToken);

        var provider = await DetectProviderAsync(
            adbPath,
            serial,
            mode,
            cancellationToken);

        var capabilities = await ProbeCapabilitiesAsync(
            adbPath,
            serial,
            mode,
            cancellationToken);

        var fullEnough =
            effectiveUid == 0 &&
            capabilities.Any(x =>
                x.Id == RootCapabilityIds.PrivateAppData &&
                x.State == CapabilityState.Verified) &&
            capabilities.Any(x =>
                x.Id == RootCapabilityIds.ProcessInspection &&
                x.State == CapabilityState.Verified);

        var state = mode == RootExecutionMode.AdbdRoot
            ? RootAccessState.AdbdRoot
            : fullEnough
                ? RootAccessState.Granted
                : RootAccessState.GrantedRestricted;

        var result = passive with
        {
            State = state,
            Provider = provider.Provider,
            ProviderVersion = provider.Version,
            EffectiveUid = effectiveUid,
            PrivilegeProfile = profile,
            Capabilities = capabilities,
            CheckedAt = DateTimeOffset.UtcNow,
            FromCachedVerification = false,
            Message = state switch
            {
                RootAccessState.AdbdRoot =>
                    "ADB daemon is running as root and privileged capabilities were verified.",
                RootAccessState.Granted =>
                    "Root access was granted and the core privileged capability probes passed.",
                _ =>
                    "su executed successfully, but REX detected a restricted privilege profile or missing core capability."
            }
        };

        _state.Set(new RootSessionCache(
            serial,
            passive.BootId,
            result.State,
            result.Provider,
            result.ProviderVersion,
            result.PrivilegeProfile,
            result.Capabilities,
            result.CheckedAt));

        return result;
    }

    public async Task<AndroidCommandResult> ExecuteVerifiedAsync(
        string serial,
        PrivilegedCommand command,
        CancellationToken cancellationToken = default)
    {
        _policy.EnsureAllowed(command.Risk);

        var passive = await ProbePassiveAsync(serial, cancellationToken);
        var mode = passive.State switch
        {
            RootAccessState.AdbdRoot => RootExecutionMode.AdbdRoot,
            RootAccessState.Granted or RootAccessState.GrantedRestricted => RootExecutionMode.Su,
            _ => throw new InvalidOperationException(
                "Root access is not verified for this Android boot. Run 'rex root request' first.")
        };

        var (adbPath, _) = await GetAdbContextAsync(serial, cancellationToken);
        var result = await _shell.RunAsync(
            adbPath,
            serial,
            command,
            mode,
            cancellationToken);

        if (result.TimedOut && mode == RootExecutionMode.Su)
        {
            _state.Remove(serial);
            throw new InvalidOperationException(
                "The privileged command timed out. Root authorization may have changed; run 'rex root request' again.");
        }

        return result;
    }

    public void ClearCachedSession(string serial) => _state.Remove(serial);

    private async Task<RootPrivilegeProfile> ProbePrivilegeProfileAsync(
        string adbPath,
        string serial,
        RootExecutionMode mode,
        int? effectiveUid,
        CancellationToken cancellationToken)
    {
        var gid = await ReadIntAsync(
            adbPath,
            serial,
            Command("root.profile.gid", "id", "-g"),
            cancellationToken,
            mode);

        var groupsText = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.profile.groups", "id", "-G"),
            cancellationToken,
            mode);

        var idText = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.profile.id", "id"),
            cancellationToken,
            mode);

        var statusText = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.profile.proc-status", "cat", "/proc/self/status"),
            cancellationToken,
            mode);

        var groups = groupsText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseInt)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        var context = ContextRegex().Match(idText).Groups["value"].Value;

        return new RootPrivilegeProfile(
            effectiveUid,
            gid,
            groups,
            context,
            ReadProcStatusValue(statusText, "CapEff"),
            ReadProcStatusValue(statusText, "CapPrm"),
            ReadProcStatusValue(statusText, "CapBnd"));
    }

    private async Task<(RootProvider Provider, string Version)> DetectProviderAsync(
        string adbPath,
        string serial,
        RootExecutionMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == RootExecutionMode.AdbdRoot)
            return (RootProvider.None, string.Empty);

        var version = await ReadTextAsync(
            adbPath,
            serial,
            Command("root.provider.su-version", "su", "-v"),
            cancellationToken);

        if (version.Contains("magisk", StringComparison.OrdinalIgnoreCase))
            return (RootProvider.Magisk, FirstLine(version));

        if (version.Contains("kernelsu", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("ksu", StringComparison.OrdinalIgnoreCase))
            return (RootProvider.KernelSU, FirstLine(version));

        if (version.Contains("apatch", StringComparison.OrdinalIgnoreCase))
            return (RootProvider.APatch, FirstLine(version));

        if (await RootTestAsync(adbPath, serial, mode, "-e", "/data/adb/magisk.db", cancellationToken) ||
            await RootCommandExistsAsync(adbPath, serial, mode, "magisk", cancellationToken))
            return (RootProvider.Magisk, FirstLine(version));

        if (await RootTestAsync(adbPath, serial, mode, "-d", "/data/adb/ksu", cancellationToken))
            return (RootProvider.KernelSU, FirstLine(version));

        if (await RootTestAsync(adbPath, serial, mode, "-d", "/data/adb/ap", cancellationToken))
            return (RootProvider.APatch, FirstLine(version));

        return (RootProvider.Other, FirstLine(version));
    }

    private async Task<IReadOnlyList<RootCapability>> ProbeCapabilitiesAsync(
        string adbPath,
        string serial,
        RootExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var probes = new[]
        {
            (RootCapabilityIds.PrivateAppData, "Private application data", Command("root.cap.private-app-data", "test", "-r", "/data/system/packages.xml")),
            (RootCapabilityIds.ProcessInspection, "Full process inspection", Command("root.cap.process-inspection", "test", "-r", "/proc/1/status")),
            (RootCapabilityIds.KernelLogs, "Kernel log access", Command("root.cap.kernel-logs", "dmesg")),
            (RootCapabilityIds.SystemFiles, "Android system data files", Command("root.cap.system-files", "test", "-d", "/data/system")),
            (RootCapabilityIds.HardwareTelemetry, "Kernel hardware telemetry", Command("root.cap.hardware", "test", "-d", "/sys/class/thermal")),
            (RootCapabilityIds.NetworkDiagnostics, "Kernel network diagnostics", Command("root.cap.network", "test", "-r", "/proc/net/tcp")),
            (RootCapabilityIds.SystemProperties, "System property inspection", Command("root.cap.properties", "getprop", "ro.build.version.release"))
        };

        var results = new List<RootCapability>();
        foreach (var (id, detail, command) in probes)
        {
            var result = await _shell.RunAsync(
                adbPath,
                serial,
                command with { MaxOutputCharacters = 8192 },
                mode,
                cancellationToken);

            results.Add(new RootCapability(
                id,
                result.Ok ? CapabilityState.Verified : CapabilityState.Unsupported,
                PrivilegeRisk.ReadOnly,
                detail));
        }

        return results;
    }

    private async Task<bool> RootTestAsync(
        string adbPath,
        string serial,
        RootExecutionMode mode,
        string flag,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await _shell.RunAsync(
            adbPath,
            serial,
            Command("root.provider.test", "test", flag, path),
            mode,
            cancellationToken);
        return result.Ok;
    }

    private async Task<bool> RootCommandExistsAsync(
        string adbPath,
        string serial,
        RootExecutionMode mode,
        string executable,
        CancellationToken cancellationToken)
    {
        var result = await _shell.RunAsync(
            adbPath,
            serial,
            Command(
                "root.provider.command",
                "sh",
                "-c",
                $"command -v {AndroidShellQuoting.Quote(executable)} >/dev/null 2>&1"),
            mode,
            cancellationToken);
        return result.Ok;
    }

    private async Task<(string AdbPath, RexDevice Device)> GetAdbContextAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        var status = await _bridge.GetStatusAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(status.AdbPath))
            throw new InvalidOperationException("adb.exe is unavailable. Run REX setup first.");

        var device = status.Devices.FirstOrDefault(x =>
            x.Serial.Equals(serial, StringComparison.Ordinal));

        if (device is null)
            throw new InvalidOperationException(
                $"Android device '{serial}' is not currently connected.");

        return (status.AdbPath, device);
    }

    private async Task<string> ReadTextAsync(
        string adbPath,
        string serial,
        PrivilegedCommand command,
        CancellationToken cancellationToken,
        RootExecutionMode? mode = null)
    {
        var result = await _shell.RunAsync(
            adbPath,
            serial,
            command,
            mode,
            cancellationToken);

        return result.Ok ? result.StdOut.Trim() : string.Empty;
    }

    private async Task<int?> ReadIntAsync(
        string adbPath,
        string serial,
        PrivilegedCommand command,
        CancellationToken cancellationToken,
        RootExecutionMode? mode = null)
    {
        var text = await ReadTextAsync(
            adbPath,
            serial,
            command,
            cancellationToken,
            mode);
        return ParseInt(text);
    }

    private static PrivilegedCommand Command(
        string id,
        string executable,
        params string[] arguments) =>
        new(id, executable, arguments, PrivilegeRisk.ReadOnly);

    private static int? ParseInt(string value) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static string ReadProcStatusValue(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!line.StartsWith(key + ":", StringComparison.Ordinal))
                continue;
            return line[(key.Length + 1)..].Trim();
        }
        return string.Empty;
    }

    private static string FirstLine(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;

    private static string DenialMessage(AndroidCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StdErr)
            ? result.StdOut.Trim()
            : result.StdErr.Trim();

        return string.IsNullOrWhiteSpace(detail)
            ? "The root provider denied or failed the authorization request."
            : $"The root provider denied or failed the authorization request: {detail}";
    }

    private static RootStatus EmptyStatus(
        string serial,
        RootAccessState state,
        string message) =>
        new(
            serial,
            state,
            RootProvider.None,
            string.Empty,
            null,
            null,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            Array.Empty<RootCapability>(),
            DateTimeOffset.UtcNow,
            false,
            message);

    private static RootStatus FromCache(
        RootSessionCache cached,
        int? adbUid,
        bool suVisible,
        string suPath,
        string selinux) =>
        new(
            cached.Serial,
            cached.State,
            cached.Provider,
            cached.ProviderVersion,
            adbUid,
            cached.PrivilegeProfile?.EffectiveUid,
            suVisible,
            suPath,
            cached.BootId,
            selinux,
            cached.PrivilegeProfile,
            cached.Capabilities,
            cached.VerifiedAt,
            true,
            "Root access was verified earlier during this Android boot. Privileged commands will revalidate by execution.");

    [GeneratedRegex(@"(?:^|\s)context=(?<value>\S+)")]
    private static partial Regex ContextRegex();
}

public static class RootCapabilityIds
{
    public const string PrivateAppData = "root.private-app-data";
    public const string ProcessInspection = "root.process-inspection";
    public const string KernelLogs = "root.kernel-logs";
    public const string SystemFiles = "root.system-files";
    public const string HardwareTelemetry = "root.hardware-telemetry";
    public const string NetworkDiagnostics = "root.network-diagnostics";
    public const string SystemProperties = "root.system-properties";
}
