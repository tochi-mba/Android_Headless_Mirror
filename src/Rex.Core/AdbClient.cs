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

    public AdbClient(string adbPath, IProcessRunner runner)
    {
        AdbPath = adbPath;
        _runner = runner;
    }

    public string AdbPath { get; }

    public Task<ProcessResult> StartServerAsync(CancellationToken cancellationToken = default) =>
        _runner.RunAsync(AdbPath, ["start-server"], TimeSpan.FromSeconds(30), cancellationToken);

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(AdbPath, ["devices", "-l"], QuickTimeout, cancellationToken).ConfigureAwait(false);
        return AdbParsing.ParseDevices(result.StdOut);
    }

    public Task<ProcessResult> ShellAsync(string serial, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        var args = new List<string> { "-s", serial, "shell" };
        args.AddRange(arguments);
        return _runner.RunAsync(AdbPath, args, timeout ?? ShellTimeout, cancellationToken);
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

    /// <summary>Applies one of the friendly device settings (brightness, dark mode, Wi-Fi...).</summary>
    public async Task<AndroidResult> ApplyFriendlySettingAsync(string serial, string id, string value, CancellationToken cancellationToken = default)
    {
        var validation = FriendlySettings.Validate(id, value);
        if (!validation.Ok)
        {
            return validation;
        }

        var command = FriendlySettings.Command(id, value);
        if (command.Kind == FriendlyCommandKind.SettingsPut)
        {
            return await PutSettingAsync(serial, command.Namespace!, command.Key!, command.Value, cancellationToken).ConfigureAwait(false);
        }

        var result = await ShellAsync(serial, command.ShellArguments, cancellationToken).ConfigureAwait(false);
        return AndroidResult.From(result);
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

    public async Task<IReadOnlyDictionary<string, string>> GetFriendlyStateAsync(string serial, CancellationToken cancellationToken = default)
    {
        // One remote shell round trip for every key; each 'settings get' is cheap on-device.
        var script = string.Join(" ; ", FriendlySettings.StateProbes.Select(probe =>
            $"echo {probe.Name}=$({probe.Command})"));
        var result = await ShellCommandAsync(serial, script, cancellationToken, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        return AdbParsing.ParseKeyValueLines(result.StdOut);
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
        _runner.RunAsync(AdbPath, ["-s", serial, "tcpip", port.ToString(CultureInfo.InvariantCulture)], QuickTimeout, cancellationToken);

    public Task<ProcessResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default) =>
        _runner.RunAsync(AdbPath, ["connect", endpoint], QuickTimeout, cancellationToken);

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

    public static IReadOnlyDictionary<string, string> ParseKeyValueLines(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 1)
            {
                continue;
            }

            values[line[..equals]] = line[(equals + 1)..].Trim();
        }

        return values;
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

public enum FriendlyCommandKind
{
    SettingsPut,
    Shell,
}

public sealed record FriendlyCommand(FriendlyCommandKind Kind, string? Namespace, string? Key, string Value, IReadOnlyList<string> ShellArguments);

public sealed record FriendlyProbe(string Name, string Command);

/// <summary>The curated "Phone" settings: id, validation and the ADB command behind each one.</summary>
public static class FriendlySettings
{
    public static readonly string[] Ids =
    [
        "brightness", "brightness-mode", "screen-timeout-ms", "auto-rotate", "user-rotation",
        "font-scale", "show-touches", "stay-awake", "animation-scale", "dark-mode",
        "wifi", "mobile-data", "airplane-mode", "wm-size", "wm-density",
    ];

    public static readonly IReadOnlyList<FriendlyProbe> StateProbes =
    [
        new("Brightness", "settings get system screen_brightness"),
        new("BrightnessMode", "settings get system screen_brightness_mode"),
        new("ScreenTimeoutMs", "settings get system screen_off_timeout"),
        new("AutoRotate", "settings get system accelerometer_rotation"),
        new("UserRotation", "settings get system user_rotation"),
        new("FontScale", "settings get system font_scale"),
        new("ShowTouches", "settings get system show_touches"),
        new("StayAwake", "settings get global stay_on_while_plugged_in"),
        new("WindowAnimation", "settings get global window_animation_scale"),
        new("UiMode", "cmd uimode night"),
        new("WmSize", "wm size | tr '\\n' ' '"),
        new("WmDensity", "wm density | tr '\\n' ' '"),
    ];

    public static AndroidResult Validate(string id, string value)
    {
        return id switch
        {
            "brightness" => Range(value, 1, 255, "Brightness must be 1-255."),
            "brightness-mode" => OneOf(value, ["0", "1"], "Brightness mode must be 0 (manual) or 1 (automatic)."),
            "screen-timeout-ms" => Range(value, 5000, 86_400_000, "Screen timeout must be between 5 seconds and 24 hours."),
            "auto-rotate" => OneOf(value, ["0", "1"], "Auto rotate must be 0 or 1."),
            "user-rotation" => OneOf(value, ["0", "1", "2", "3"], "Rotation must be 0, 1, 2 or 3."),
            "font-scale" => Range(value, 0.5, 2.0, "Font scale must be between 0.5 and 2.0."),
            "show-touches" => OneOf(value, ["0", "1"], "Show touches must be 0 or 1."),
            "stay-awake" => OneOf(value, ["0", "1", "2", "4", "7"], "Stay awake must be 0, 1, 2, 4 or 7."),
            "animation-scale" => Range(value, 0, 10, "Animation scale must be between 0 and 10."),
            "dark-mode" => OneOf(value, ["yes", "no", "auto"], "Dark mode must be yes, no or auto."),
            "wifi" or "mobile-data" or "airplane-mode" => OneOf(value, ["enable", "disable"], "Use enable or disable."),
            "wm-size" => value == "reset" || Regex.IsMatch(value, @"^\d{3,5}x\d{3,5}$")
                ? AndroidResult.Success()
                : AndroidResult.Failure("Display size must look like 1080x2400, or reset."),
            "wm-density" => value == "reset"
                ? AndroidResult.Success()
                : Range(value, 120, 1000, "Display density must be 120-1000, or reset."),
            _ => AndroidResult.Failure($"Unknown phone setting '{id}'."),
        };
    }

    public static FriendlyCommand Command(string id, string value)
    {
        return id switch
        {
            "brightness" => Put("system", "screen_brightness", ((int)double.Parse(value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)),
            "brightness-mode" => Put("system", "screen_brightness_mode", value),
            "screen-timeout-ms" => Put("system", "screen_off_timeout", ((long)double.Parse(value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)),
            "auto-rotate" => Put("system", "accelerometer_rotation", value),
            "user-rotation" => Put("system", "user_rotation", value),
            "font-scale" => Put("system", "font_scale", value),
            "show-touches" => Put("system", "show_touches", value),
            "stay-awake" => Put("global", "stay_on_while_plugged_in", value),
            "animation-scale" => Shell("sh", "-c",
                $"settings put global window_animation_scale {ShellQuoting.Quote(value)} && " +
                $"settings put global transition_animation_scale {ShellQuoting.Quote(value)} && " +
                $"settings put global animator_duration_scale {ShellQuoting.Quote(value)}"),
            "dark-mode" => Shell("cmd", "uimode", "night", value),
            "wifi" => Shell("svc", "wifi", value),
            "mobile-data" => Shell("svc", "data", value),
            "airplane-mode" => Shell("cmd", "connectivity", "airplane-mode", value),
            "wm-size" => Shell("wm", "size", value),
            "wm-density" => Shell("wm", "density", value),
            _ => throw new ArgumentException($"Unknown phone setting '{id}'.", nameof(id)),
        };
    }

    private static FriendlyCommand Put(string ns, string key, string value) =>
        new(FriendlyCommandKind.SettingsPut, ns, key, value, []);

    private static FriendlyCommand Shell(params string[] args) =>
        new(FriendlyCommandKind.Shell, null, null, string.Empty, args);

    private static AndroidResult Range(string value, double min, double max, string message) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
            ? AndroidResult.Success()
            : AndroidResult.Failure(message);

    private static AndroidResult OneOf(string value, string[] allowed, string message) =>
        allowed.Contains(value, StringComparer.Ordinal) ? AndroidResult.Success() : AndroidResult.Failure(message);
}
