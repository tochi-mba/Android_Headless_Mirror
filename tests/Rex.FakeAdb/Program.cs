using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// A deterministic stand-in for adb.exe. The scenario (devices, properties, settings) comes
// from the JSON file named by REX_FAKE_ADB_SCENARIO; every invocation is appended to
// REX_FAKE_ADB_LOG so tests can assert exactly what the app asked the phone to do.

// Defaults live next to the executable so every test root is isolated; env vars override.
var log = Environment.GetEnvironmentVariable("REX_FAKE_ADB_LOG") ?? Path.Combine(AppContext.BaseDirectory, "fake-adb.log");
AppendLine(log, string.Join(' ', args.Select(Quote)));

var scenario = Scenario.Load(Environment.GetEnvironmentVariable("REX_FAKE_ADB_SCENARIO") ?? Path.Combine(AppContext.BaseDirectory, "fake-adb.json"));
var stdout = Console.OpenStandardOutput();

static string Quote(string arg) => arg.Contains(' ', StringComparison.Ordinal) ? $"\"{arg}\"" : arg;

// The app runs several adb calls at once (device poll, battery poll, actions), so appenders
// must share the file; a brief retry covers the window where one holds it exclusively.
static void AppendLine(string path, string line)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            stream.Write(Encoding.UTF8.GetBytes(line + Environment.NewLine));
            return;
        }
        catch (IOException) when (attempt < 20)
        {
            Thread.Sleep(10);
        }
    }
}

int Write(string text, int code = 0)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    stdout.Write(bytes, 0, bytes.Length);
    stdout.Flush();
    return code;
}

if (args.Length == 0)
{
    return Write("Android Debug Bridge version 1.0.41 (fake)\n");
}

var i = 0;
string? serial = null;
if (args[i] == "-s" && args.Length > i + 1)
{
    serial = args[i + 1];
    i += 2;
}

var command = args.Length > i ? args[i] : string.Empty;
var rest = args.Skip(i + 1).ToArray();

switch (command)
{
    case "version":
        return Write("Android Debug Bridge version 1.0.41 (fake)\nVersion 35.0.2-fake\n");

    case "start-server":
    case "kill-server":
        return 0;

    case "devices":
    {
        var builder = new StringBuilder("List of devices attached\n");
        foreach (var device in scenario.Devices)
        {
            builder.Append(device.Serial).Append("       ").Append(device.State);
            if (device.State == "device")
            {
                builder.Append(" product:").Append(device.Product).Append(" model:").Append(device.Model.Replace(' ', '_')).Append(" device:x transport_id:1");
            }
            else
            {
                builder.Append(" transport_id:2");
            }

            builder.Append('\n');
        }

        builder.Append('\n');
        return Write(builder.ToString());
    }

    case "tcpip":
        return Write($"restarting in TCP mode port: {(rest.Length > 0 ? rest[0] : "5555")}\n");

    case "connect":
        return Write($"connected to {(rest.Length > 0 ? rest[0] : "")}\n");

    case "exec-out":
        return ExecOut(rest);

    case "shell":
        return Shell(rest);

    default:
        Console.Error.WriteLine($"fake adb: unknown command '{command}'");
        return 1;
}

int ExecOut(string[] rest)
{
    if (rest.Length >= 2 && rest[0] == "screencap")
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "preview.png");
        var bytes = File.Exists(fixture) ? File.ReadAllBytes(fixture) : Scenario.TinyPng;
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
        return 0;
    }

    if (rest.Length >= 2 && rest[0] == "uiautomator" && rest[1] == "dump")
    {
        if (scenario.UiHierarchyDelayMs > 0)
        {
            Thread.Sleep(scenario.UiHierarchyDelayMs);
        }

        return Write("UI hierchary dumped to: /dev/tty\n" + scenario.UiHierarchy + "\n");
    }

    return Shell(rest);
}

int Shell(string[] rest)
{
    var device = scenario.Devices.FirstOrDefault(d => d.Serial == serial) ?? scenario.Devices.FirstOrDefault(d => d.State == "device");
    if (device is null)
    {
        Console.Error.WriteLine("error: device not found");
        return 1;
    }

    if (device.State != "device")
    {
        Console.Error.WriteLine($"error: device {device.State}");
        return 1;
    }

    var line = string.Join(' ', rest);

    if (rest.Length == 0)
    {
        return 0;
    }

    if (rest[0] == "getprop")
    {
        if (rest.Length == 1)
        {
            return Write(string.Join('\n', scenario.Properties.Select(p => $"[{p.Key}]: [{p.Value}]")) + "\n");
        }

        return Write((scenario.Properties.TryGetValue(rest[1], out var value) ? value : string.Empty) + "\n");
    }

    if (rest[0] == "wm" && rest.Length >= 2 && rest[1] == "size")
    {
        if (rest.Length >= 3)
        {
            scenario.Overrides["wm.size"] = rest[2];
            scenario.Save();
            return 0;
        }

        var text = $"Physical size: {scenario.DisplayWidth}x{scenario.DisplayHeight}\n";
        if (scenario.Overrides.TryGetValue("wm.size", out var over) && over != "reset")
        {
            text += $"Override size: {over}\n";
        }

        return Write(text);
    }

    if (rest[0] == "wm" && rest.Length >= 2 && rest[1] == "density")
    {
        return rest.Length >= 3 ? 0 : Write("Physical density: 515\n");
    }

    if (rest[0] == "wm")
    {
        return 0;
    }

    if (rest[0] == "settings")
    {
        return Settings(rest);
    }

    if (rest[0] == "dumpsys" && rest.Length >= 2 && rest[1] == "battery")
    {
        return Write("Current Battery Service state:\n  AC powered: false\n  USB powered: true\n  status: 2\n  level: 74\n  scale: 100\n");
    }

    if (rest[0] is "input" or "cmd" or "svc" or "dumpsys")
    {
        return 0;
    }

    // Everything else is a one-line script (sh -c style) sent as a single argument.
    if (line.Contains("echo ", StringComparison.Ordinal) && line.Contains("=$(", StringComparison.Ordinal))
    {
        return Write(RunStateScript(line));
    }

    if (line.Contains("dumpsys window", StringComparison.Ordinal))
    {
        return Write(scenario.KeyguardLocked ? "    mShowingLockscreen=true\n" : "    mShowingLockscreen=false\n");
    }

    if (line.StartsWith("settings put", StringComparison.Ordinal))
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Settings(parts);
    }

    return 0;
}

int Settings(string[] parts)
{
    // settings <verb> <ns> [key] [value]
    if (parts.Length < 3)
    {
        return 1;
    }

    var verb = parts[1];
    var ns = parts[2];
    switch (verb)
    {
        case "list":
            return Write(string.Join('\n', scenario.SettingsFor(ns).Select(p => $"{p.Key}={p.Value}")) + "\n");
        case "get":
            return Write((scenario.SettingsFor(ns).TryGetValue(parts[3], out var current) ? current : "null") + "\n");
        case "put":
        {
            var value = string.Join(' ', parts.Skip(4)).Trim('\'');
            scenario.SettingsFor(ns)[parts[3]] = value;
            scenario.Save();
            return 0;
        }
        case "delete":
            scenario.SettingsFor(ns).Remove(parts[3]);
            scenario.Save();
            return Write("Deleted 1 rows\n");
        default:
            return 1;
    }
}

string RunStateScript(string script)
{
    var builder = new StringBuilder();
    foreach (var piece in script.Split(" ; ", StringSplitOptions.RemoveEmptyEntries))
    {
        var trimmed = piece.Trim();
        if (!trimmed.StartsWith("echo ", StringComparison.Ordinal))
        {
            continue;
        }

        var equals = trimmed.IndexOf("=$(", StringComparison.Ordinal);
        var name = trimmed[5..equals];
        var inner = trimmed[(equals + 3)..].TrimEnd(')');
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string value = string.Empty;
        if (parts.Length >= 4 && parts[0] == "settings" && parts[1] == "get")
        {
            value = scenario.SettingsFor(parts[2]).TryGetValue(parts[3], out var v) ? v : "null";
        }
        else if (inner.StartsWith("cmd uimode", StringComparison.Ordinal))
        {
            value = "Night mode: " + (scenario.SettingsFor("secure").TryGetValue("ui_night_mode", out var n) && n == "2" ? "yes" : "no");
        }
        else if (inner.StartsWith("wm size", StringComparison.Ordinal))
        {
            value = $"Physical size: {scenario.DisplayWidth}x{scenario.DisplayHeight}";
        }
        else if (inner.StartsWith("wm density", StringComparison.Ordinal))
        {
            value = "Physical density: 515";
        }

        builder.Append(name).Append('=').Append(value).Append('\n');
    }

    return builder.ToString();
}

internal sealed class FakeDevice
{
    public string Serial { get; set; } = "FAKE123";
    public string State { get; set; } = "device";
    public string Product { get; set; } = "fake";
    public string Model { get; set; } = "Fake Phone";
}

internal sealed class Scenario
{
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public string? Path { get; private set; }
    public List<FakeDevice> Devices { get; set; } = [];
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);
    public int DisplayWidth { get; set; } = 1080;
    public int DisplayHeight { get; set; } = 2400;
    public bool KeyguardLocked { get; set; }
    public int UiHierarchyDelayMs { get; set; }
    public string UiHierarchy { get; set; } = "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\"><node class=\"android.widget.FrameLayout\" bounds=\"[0,0][1080,2400]\" /></hierarchy>";

    public Dictionary<string, string> SettingsFor(string ns)
    {
        if (!Settings.TryGetValue(ns, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            Settings[ns] = values;
        }

        return values;
    }

    public static Scenario Load(string? path)
    {
        Scenario scenario;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            scenario = JsonSerializer.Deserialize(File.ReadAllText(path), ScenarioJson.Default.Scenario) ?? Default();
            scenario.Path = path;
        }
        else
        {
            scenario = Default();
        }

        return scenario;
    }

    public void Save()
    {
        if (Path is null)
        {
            return;
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(this, ScenarioJson.Default.Scenario));
    }

    public static Scenario Default() => new()
    {
        Devices = [new FakeDevice { Serial = "FAKE123", Model = "Fake Phone" }],
        Properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ro.product.manufacturer"] = "Samsung",
            ["ro.product.model"] = "SM-G998B",
            ["ro.product.marketname"] = "Galaxy S21 Ultra",
            ["ro.build.version.release"] = "15",
            ["ro.build.version.sdk"] = "35",
        },
        Settings = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["system"] = new(StringComparer.Ordinal)
            {
                ["screen_brightness"] = "128",
                ["screen_brightness_mode"] = "0",
                ["screen_off_timeout"] = "60000",
                ["accelerometer_rotation"] = "1",
                ["user_rotation"] = "0",
                ["font_scale"] = "1.0",
                ["show_touches"] = "0",
            },
            ["secure"] = new(StringComparer.Ordinal) { ["adb_enabled"] = "1", ["ui_night_mode"] = "1" },
            ["global"] = new(StringComparer.Ordinal) { ["stay_on_while_plugged_in"] = "7", ["window_animation_scale"] = "1", ["adb_enabled"] = "1" },
        },
    };
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Scenario))]
internal sealed partial class ScenarioJson : System.Text.Json.Serialization.JsonSerializerContext;
