using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Core;

public sealed record AdbDevice(string Serial, string State, bool IsTcp, string Product, string Model)
{
    public bool IsReady => State == "device";
    public bool IsUnauthorized => State == "unauthorized";
    public string Transport => IsTcp ? "wireless" : "USB";
}

public sealed record DeviceIdentity(
    string Serial,
    string Manufacturer,
    string Model,
    string MarketingName,
    string AndroidVersion,
    string ApiLevel,
    int DisplayWidth,
    int DisplayHeight)
{
    /// <summary>"Galaxy S21 Ultra", else "Samsung SM-G998B", else the serial.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MarketingName))
            {
                return MarketingName;
            }

            var pieces = new[] { Manufacturer, Model }.Where(x => !string.IsNullOrWhiteSpace(x));
            var joined = string.Join(' ', pieces).Trim();
            return joined.Length > 0 ? joined : Serial;
        }
    }
}

public sealed record BatteryStatus(int Level, bool Charging);

public sealed record AndroidResult(bool Ok, string Text)
{
    public static AndroidResult Success(string text = "") => new(true, text);
    public static AndroidResult Failure(string text) => new(false, text);
    public static AndroidResult From(ProcessResult result) =>
        result.Ok ? Success(result.StdOut.Trim()) : Failure(result.FailureText);
}

public sealed record AndroidSettingRow(string Namespace, string Key, string Value, string Risk);

public enum KeyguardState
{
    Unknown,
    Locked,
    Unlocked,
}

/// <summary>Everything the app asks ADB for, with one place for quoting, timeouts and output parsing.</summary>
public sealed class AdbClient
{
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _runner;
    private readonly object _serverGate = new();
    private Task? _serverStarted;

    public AdbClient(string adbPath, IProcessRunner runner)
    {
        AdbPath = adbPath;
        _runner = runner;
    }

    public string AdbPath { get; }

    /// <summary>
    /// Starts the ADB server (a no-op when it already runs) without capturing output. The
    /// server is a daemon that inherits the pipes of the adb call that spawned it, so a captured
    /// call could never see end-of-file; every other command goes through here first.
    /// </summary>
    public Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        lock (_serverGate)
        {
            return _serverStarted ??= _runner.RunDetachedAsync(AdbPath, ["start-server"], TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await StartServerAsync(cancellationToken).ConfigureAwait(false);
        return await _runner.RunAsync(AdbPath, arguments, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["devices", "-l"], QuickTimeout, cancellationToken).ConfigureAwait(false);
        return AdbParsing.ParseDevices(result.StdOut);
    }

    public Task<ProcessResult> ShellAsync(string serial, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        var args = new List<string> { "-s", serial, "shell" };
        args.AddRange(arguments);
        return RunAsync(args, timeout ?? ShellTimeout, cancellationToken);
    }

    /// <summary>Runs one remote shell command line (already quoted for the device shell).</summary>
    public Task<ProcessResult> ShellCommandAsync(string serial, string commandLine, CancellationToken cancellationToken = default, TimeSpan? timeout = null) =>
        ShellAsync(serial, [commandLine], cancellationToken, timeout);

    public async Task<DeviceIdentity> GetIdentityAsync(string serial, CancellationToken cancellationToken = default)
    {
        var props = await GetPropertiesAsync(serial, cancellationToken).ConfigureAwait(false);
        var size = await GetDisplaySizeAsync(serial, cancellationToken).ConfigureAwait(false);

        return new DeviceIdentity(
            serial,
            Prop(props, "ro.product.manufacturer"),
            Prop(props, "ro.product.model"),
            AdbParsing.PickMarketingName(props),
            Prop(props, "ro.build.version.release"),
            Prop(props, "ro.build.version.sdk"),
            size.Width,
            size.Height);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await ShellAsync(serial, ["getprop"], cancellationToken).ConfigureAwait(false);
        return AdbParsing.ParseProperties(result.StdOut);
    }

    public async Task<(int Width, int Height)> GetDisplaySizeAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await ShellAsync(serial, ["wm", "size"], cancellationToken).ConfigureAwait(false);
        return AdbParsing.ParseWmSize(result.StdOut);
    }

    public async Task<BatteryStatus?> GetBatteryAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await ShellAsync(serial, ["dumpsys", "battery"], cancellationToken).ConfigureAwait(false);
        return result.Ok ? AdbParsing.ParseBattery(result.StdOut) : null;
    }

    public async Task<AndroidResult> KeyEventAsync(string serial, string keycode, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(keycode, "^[A-Z0-9_]+$"))
        {
            return AndroidResult.Failure("Invalid key code.");
        }

        var result = await ShellAsync(serial, ["input", "keyevent", keycode], cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public async Task<AndroidResult> StatusBarAsync(string serial, string verb, CancellationToken cancellationToken = default)
    {
        if (verb is not ("expand-notifications" or "expand-settings" or "collapse"))
        {
            return AndroidResult.Failure("Unsupported status bar action.");
        }

        var result = await ShellAsync(serial, ["cmd", "statusbar", verb], cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public Task<AndroidResult> WakeAsync(string serial, CancellationToken cancellationToken = default) =>
        KeyEventAsync(serial, "KEYCODE_WAKEUP", cancellationToken);

    public async Task<AndroidResult> DismissKeyguardAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await ShellAsync(serial, ["wm", "dismiss-keyguard"], cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public async Task<byte[]?> ScreencapAsync(string serial, CancellationToken cancellationToken = default)
    {
        await StartServerAsync(cancellationToken).ConfigureAwait(false);
        var result = await _runner.RunBytesAsync(AdbPath, ["-s", serial, "exec-out", "screencap", "-p"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return result.Ok && result.StdOut.Length > 8 ? result.StdOut : null;
    }

    // ----- Settings provider -----

    public async Task<AndroidResult> GetSettingAsync(string serial, string ns, string key, CancellationToken cancellationToken = default)
    {
        if (!AndroidSettings.IsValidNamespace(ns) || !AndroidSettings.IsValidKey(key))
        {
            return AndroidResult.Failure("Invalid settings namespace or key.");
        }

        var result = await ShellAsync(serial, ["settings", "get", ns, key], cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public async Task<AndroidResult> PutSettingAsync(string serial, string ns, string key, string value, CancellationToken cancellationToken = default)
    {
        if (!AndroidSettings.IsValidNamespace(ns) || !AndroidSettings.IsValidKey(key))
        {
            return AndroidResult.Failure("Invalid settings namespace or key.");
        }

        if (AndroidSettings.Risk(ns, key) == AndroidSettings.RiskProtected)
        {
            return AndroidResult.Failure($"'{key}' is protected because changing it could cut off ADB or change the device identity.");
        }

        if (value.Length > 8192 || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return AndroidResult.Failure("Unsupported settings value.");
        }

        var command = $"settings put {ns} {key} {ShellQuoting.Quote(value)}";
        var result = await ShellCommandAsync(serial, command, cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public async Task<AndroidResult> DeleteSettingAsync(string serial, string ns, string key, CancellationToken cancellationToken = default)
    {
        if (!AndroidSettings.IsValidNamespace(ns) || !AndroidSettings.IsValidKey(key))
        {
            return AndroidResult.Failure("Invalid settings namespace or key.");
        }

        if (AndroidSettings.Risk(ns, key) == AndroidSettings.RiskProtected)
        {
            return AndroidResult.Failure($"'{key}' is protected from deletion.");
        }

        var result = await ShellAsync(serial, ["settings", "delete", ns, key], cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
    }

    public async Task<(bool Ok, string Error, IReadOnlyList<AndroidSettingRow> Rows)> ListSettingsAsync(string serial, string ns, CancellationToken cancellationToken = default)
    {
        if (!AndroidSettings.IsValidNamespace(ns))
        {
            return (false, "Namespace must be system, secure or global.", []);
        }

        var result = await ShellAsync(serial, ["settings", "list", ns], cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return (false, result.FailureText, []);
        }

        return (true, string.Empty, AdbParsing.ParseSettingsList(ns, result.StdOut));
    }

    /// <summary>
    /// Every catalogue setting this phone actually has, with its current value. Reads the three
    /// settings namespaces in one pass plus a probe for the few settings that are not plain keys;
    /// entries the phone does not expose are left out rather than shown as dead rows.
    /// </summary>
    public async Task<IReadOnlyList<PhoneSettingValue>> ReadPhoneSettingsAsync(string serial, CancellationToken cancellationToken = default)
    {
        var stored = new Dictionary<(string Namespace, string Key), string>();
        foreach (var ns in PhoneSettings.ReadNamespaces)
        {
            var (ok, _, rows) = await ListSettingsAsync(serial, ns, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                continue;
            }

            foreach (var row in rows)
            {
                stored[(row.Namespace, row.Key)] = row.Value;
            }
        }

        var probed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, command) in PhoneSettings.Probes)
        {
            var result = await ShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                probed[id] = PhoneSettings.ParseProbe(id, result.StdOut);
            }
        }

        var values = new List<PhoneSettingValue>();
        foreach (var setting in PhoneSettings.All)
        {
            var value = probed.TryGetValue(setting.Id, out var probe) && probe.Length > 0
                ? probe
                : stored.GetValueOrDefault((setting.Namespace, setting.Key), string.Empty);

            if (setting.AlwaysAvailable || stored.ContainsKey((setting.Namespace, setting.Key)))
            {
                values.Add(new PhoneSettingValue(setting, value));
            }
        }

        return values;
    }

    /// <summary>Writes one catalogue setting after checking the value against its own rules.</summary>
    public async Task<AndroidResult> ApplyPhoneSettingAsync(string serial, string id, string value, CancellationToken cancellationToken = default)
    {
        if (PhoneSettings.Find(id) is not { } setting)
        {
            return AndroidResult.Failure($"Unknown phone setting '{id}'.");
        }

        var validation = PhoneSettings.Validate(setting, value);
        if (!validation.Ok)
        {
            return validation;
        }

        if (setting.Source == PhoneSettingSource.SettingsProvider)
        {
            // Provider writes go through PutSettingAsync so the protected-key guard is never bypassed.
            return await PutSettingAsync(serial, setting.Namespace, setting.Key, validation.Text, cancellationToken).ConfigureAwait(false);
        }

        var result = await ShellAsync(serial, PhoneSettings.WriteCommand(setting, validation.Text), cancellationToken).ConfigureAwait(false);
        return result.Ok
            ? AndroidResult.Success($"{setting.Label}: {setting.Describe(validation.Text)}")
            : AndroidResult.Failure(result.FailureText);
    }

    /// <summary>Deletes a catalogue setting's key so Android falls back to its own default.</summary>
    public async Task<AndroidResult> ResetPhoneSettingAsync(string serial, string id, CancellationToken cancellationToken = default)
    {
        if (PhoneSettings.Find(id) is not { } setting)
        {
            return AndroidResult.Failure($"Unknown phone setting '{id}'.");
        }

        if (!setting.CanReset)
        {
            return AndroidResult.Failure($"{setting.Label} has no stored key to delete; set it to the value you want instead.");
        }

        return await DeleteSettingAsync(serial, setting.Namespace, setting.Key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forces a display rotation (0..3) or restores sensor rotation ("auto"). Reversible.</summary>
    public async Task<AndroidResult> SetRotationOverrideAsync(string serial, string mode, CancellationToken cancellationToken = default)
    {
        if (mode == "auto")
        {
            var auto = await PutSettingAsync(serial, "system", "accelerometer_rotation", "1", cancellationToken).ConfigureAwait(false);
            return auto.Ok ? AndroidResult.Success("Automatic rotation restored.") : AndroidResult.Failure("Could not restore automatic rotation: " + auto.Text);
        }

        if (mode is not ("0" or "1" or "2" or "3"))
        {
            return AndroidResult.Failure("Rotation must be auto, 0, 1, 2 or 3.");
        }

        // Set the target first, then lock, so a partial failure never strands the phone sideways.
        var rotation = await PutSettingAsync(serial, "system", "user_rotation", mode, cancellationToken).ConfigureAwait(false);
        if (!rotation.Ok)
        {
            return AndroidResult.Failure("Could not set rotation: " + rotation.Text);
        }

        var lockResult = await PutSettingAsync(serial, "system", "accelerometer_rotation", "0", cancellationToken).ConfigureAwait(false);
        if (!lockResult.Ok)
        {
            return AndroidResult.Failure("Rotation was accepted but the lock was refused: " + lockResult.Text);
        }

        var degrees = int.Parse(mode, CultureInfo.InvariantCulture) * 90;
        return AndroidResult.Success($"Rotation locked to {degrees}°.");
    }

    // ----- Lock screen support -----

    public async Task<KeyguardState> GetKeyguardStateAsync(string serial, CancellationToken cancellationToken = default)
    {
        const string script =
            "dumpsys window 2>/dev/null | grep -E 'ShowingLockscreen|KeyguardShowing|isStatusBarKeyguard|keyguardShowing|deviceLocked' ; " +
            "dumpsys trust 2>/dev/null | grep -iE 'deviceLocked'";
        var result = await ShellCommandAsync(serial, script, cancellationToken, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        return PatternGeometry.ParseKeyguardState(result.StdOut);
    }

    /// <summary>Dumps the current UI hierarchy straight to stdout. Nothing is left on the device.</summary>
    public async Task<string?> DumpUiHierarchyAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            AdbPath,
            ["-s", serial, "exec-out", "uiautomator", "dump", "--compressed", "/dev/tty"],
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        var text = result.StdOut;
        var start = text.IndexOf("<?xml", StringComparison.Ordinal);
        if (start < 0)
        {
            start = text.IndexOf("<hierarchy", StringComparison.Ordinal);
        }

        var end = text.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end < start)
        {
            return null;
        }

        return text[start..(end + "</hierarchy>".Length)];
    }

    // ----- Wireless ADB -----

    public async Task<IReadOnlyList<string>> GetPhoneIpCandidatesAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await ShellAsync(serial, ["ip", "-o", "-4", "addr", "show"], cancellationToken).ConfigureAwait(false);
        return AdbParsing.ParseIpCandidates(result.StdOut);
    }

    public Task<ProcessResult> TcpipAsync(string serial, int port, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "tcpip", port.ToString(CultureInfo.InvariantCulture)], QuickTimeout, cancellationToken);

    public Task<ProcessResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default) =>
        RunAsync(["connect", endpoint], QuickTimeout, cancellationToken);

    private static string Prop(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) ? value : string.Empty;
}

/// <summary>Quotes a value as one argument for the Android shell (POSIX sh).</summary>
public static partial class ShellQuoting
{
    [GeneratedRegex(@"^[A-Za-z0-9_@%+=:,./-]+$")]
    private static partial Regex SafeArgument();

    public static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        return SafeArgument().IsMatch(value)
            ? value
            : "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}

public static partial class AdbParsing
{
    [GeneratedRegex(@"^\s*(\S+)\s+(device|unauthorized|offline|no permissions|authorizing|connecting|recovery|sideload|bootloader|unknown)(?:\s+(.*))?$", RegexOptions.Multiline)]
    private static partial Regex DeviceLine();

    [GeneratedRegex(@"\[([^\]]*)\]:\s*\[([^\]]*)\]")]
    private static partial Regex PropertyLine();

    [GeneratedRegex(@"(?im)(Override|Physical) size:\s*(\d+)x(\d+)")]
    private static partial Regex WmSize();

    [GeneratedRegex(@"^\d+:\s+([^:\s]+).*?\sinet\s+(\d+\.\d+\.\d+\.\d+)/", RegexOptions.Multiline)]
    private static partial Regex IpLine();

    public static IReadOnlyList<AdbDevice> ParseDevices(string text)
    {
        var devices = new List<AdbDevice>();
        foreach (Match match in DeviceLine().Matches(text.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            var serial = match.Groups[1].Value;
            if (serial == "List")
            {
                continue;
            }

            var extras = match.Groups[3].Value;
            devices.Add(new AdbDevice(
                serial,
                match.Groups[2].Value,
                IsTcpSerial(serial),
                Extra(extras, "product"),
                Extra(extras, "model")));
        }

        return devices;
    }

    public static bool IsTcpSerial(string serial) => Regex.IsMatch(serial, @":\d+$");

    private static string Extra(string extras, string name)
    {
        var match = Regex.Match(extras, $@"\b{name}:(\S+)");
        return match.Success ? match.Groups[1].Value.Replace('_', ' ') : string.Empty;
    }

    public static IReadOnlyDictionary<string, string> ParseProperties(string text)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PropertyLine().Matches(text))
        {
            props[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return props;
    }

    private static readonly string[] MarketingNameKeys =
    [
        "ro.product.marketname",
        "ro.config.marketing_name",
        "ro.product.vendor.marketname",
        "ro.product.odm.marketname",
        "ro.vendor.oplus.market.name",
        "ro.oppo.market.name",
        "ro.product.system.marketname",
    ];

    public static string PickMarketingName(IReadOnlyDictionary<string, string> props)
    {
        foreach (var key in MarketingNameKeys)
        {
            if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    public static (int Width, int Height) ParseWmSize(string text)
    {
        var matches = WmSize().Matches(text);
        if (matches.Count == 0)
        {
            return (0, 0);
        }

        // Prefer an override size when present; it is the size scrcpy captures.
        var chosen = matches.Cast<Match>().LastOrDefault(m => m.Groups[1].Value.Equals("Override", StringComparison.OrdinalIgnoreCase))
            ?? matches[0];
        return (int.Parse(chosen.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(chosen.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    public static BatteryStatus? ParseBattery(string text)
    {
        var level = Regex.Match(text, @"level:\s*(\d+)");
        if (!level.Success)
        {
            return null;
        }

        var charging =
            Regex.IsMatch(text, @"(AC|USB|Wireless) powered:\s*true") ||
            Regex.IsMatch(text, @"status:\s*2\b");
        return new BatteryStatus(int.Parse(level.Groups[1].Value, CultureInfo.InvariantCulture), charging);
    }

    public static IReadOnlyList<AndroidSettingRow> ParseSettingsList(string ns, string text)
    {
        var rows = new List<AndroidSettingRow>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 1)
            {
                continue;
            }

            var key = line[..equals];
            rows.Add(new AndroidSettingRow(ns, key, line[(equals + 1)..], AndroidSettings.Risk(ns, key)));
        }

        return rows.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> ParseIpCandidates(string text)
    {
        var preferred = new List<string>();
        var fallback = new List<string>();

        foreach (Match match in IpLine().Matches(text.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            var iface = match.Groups[1].Value;
            var ip = match.Groups[2].Value;
            if (!IsPrivateIPv4(ip) || ip == "127.0.0.1")
            {
                continue;
            }

            if (!fallback.Contains(ip))
            {
                fallback.Add(ip);
            }

            if (Regex.IsMatch(iface, "(?i)(wlan|swlan|ap|softap|wifi)") && !preferred.Contains(ip))
            {
                preferred.Add(ip);
            }
        }

        return preferred.Count > 0 ? preferred : fallback;
    }

    public static bool IsPrivateIPv4(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        var bytes = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]) || bytes[i] > 255)
            {
                return false;
            }
        }

        return bytes[0] == 10
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
    }
}

/// <summary>Namespaces, key validation and the protected/sensitive risk labels for the Settings provider.</summary>
public static partial class AndroidSettings
{
    public const string RiskProtected = "protected";
    public const string RiskSensitive = "sensitive";
    public const string RiskAdvanced = "advanced";
    public const string RiskNormal = "normal";

    public static readonly string[] Namespaces = ["system", "secure", "global"];

    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "adb_enabled",
        "development_settings_enabled",
        "android_id",
        "bluetooth_address",
        "adb_wifi_enabled",
    };

    [GeneratedRegex(@"^[A-Za-z0-9._:-]+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"(^|_)(accessibility|install|unknown|verifier|mock|location|vpn|proxy|dns|airplane|data|wifi|bluetooth|package|device_provisioned|user_setup_complete)")]
    private static partial Regex SensitivePattern();

    public static bool IsValidNamespace(string ns) => Namespaces.Contains(ns, StringComparer.Ordinal);

    public static bool IsValidKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 200 && KeyPattern().IsMatch(key);

    public static string Risk(string ns, string key)
    {
        var normalized = key.ToLowerInvariant();
        if (Protected.Contains(normalized))
        {
            return RiskProtected;
        }

        if (SensitivePattern().IsMatch(normalized))
        {
            return RiskSensitive;
        }

        return ns is "secure" or "global" ? RiskAdvanced : RiskNormal;
    }
}
