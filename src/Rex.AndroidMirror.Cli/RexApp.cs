using System.Text.Json;
using Spectre.Console;

namespace Rex.AndroidMirror.Cli;

public sealed class RexApp
{
    private readonly AppPaths _paths;
    private readonly IProcessRunner _runner;
    private readonly IBridgeClient _bridge;
    private readonly ConfigStore _config;
    private readonly IAnsiConsole _console;
    private readonly bool _pauseEnabled;
    private readonly bool _precisionTouchpadEligible;

    public RexApp(
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config,
        IAnsiConsole? console = null,
        bool pauseEnabled = true,
        bool? precisionTouchpadEligible = null)
    {
        _paths = paths;
        _runner = runner;
        _bridge = bridge;
        _config = config;
        _console = console ?? AnsiConsole.Console;
        _pauseEnabled = pauseEnabled;
        _precisionTouchpadEligible = precisionTouchpadEligible ??
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    }

    public async Task<int> RunAsync()
    {
        RexStatus status;
        try
        {
            status = await GetStatusWithSpinnerAsync();
        }
        catch (Exception ex)
        {
            RexBrand.Header(_console, "RECOVERY");
            RexBrand.Error(_console, ex.Message);
            return 1;
        }

        if (!status.SetupComplete)
        {
            if (!await RunOnboardingAsync(status))
                return 1;

            status = await GetStatusWithSpinnerAsync();
        }

        while (true)
        {
            RexBrand.Header(_console);
            RenderStatus(status);

            var choice = _console.Prompt(RexBrand.Menu(
                "What do you want to do?",
                new[]
                {
                    "Start / open mirror",
                    "Runtime controls",
                    "PC / mirror settings",
                    "Device settings",
                    "Advanced Android settings",
                    "Open GUI Control Center",
                    "Diagnostics",
                    "Setup / repair",
                    "Windows startup",
                    "Captures",
                    "Stop Android Headless Mirror",
                    "Refresh",
                    "Exit",
                }));

            try
            {
                switch (choice)
                {
                    case "Start / open mirror":
                        await StartAsync();
                        break;
                    case "Runtime controls":
                        await RuntimeControlsAsync();
                        break;
                    case "PC / mirror settings":
                        await PcSettingsAsync();
                        break;
                    case "Device settings":
                        await DeviceSettingsAsync();
                        break;
                    case "Advanced Android settings":
                        await AdvancedAndroidAsync();
                        break;
                    case "Open GUI Control Center":
                        await OpenControlCenterAsync();
                        break;
                    case "Diagnostics":
                        await DiagnosticsAsync();
                        break;
                    case "Setup / repair":
                        await RepairAsync(status);
                        break;
                    case "Windows startup":
                        await AutostartAsync();
                        break;
                    case "Captures":
                        await CapturesAsync();
                        break;
                    case "Stop Android Headless Mirror":
                        await StopAsync();
                        break;
                    case "Exit":
                        return 0;
                }
            }
            catch (Exception ex)
            {
                RexBrand.Error(_console, ex.Message);
                Pause();
            }

            status = await GetStatusWithSpinnerAsync();
        }
    }

    private async Task<bool> RunOnboardingAsync(RexStatus initial)
    {
        RexBrand.Header(_console, "FIRST RUN");

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Parse(RexBrand.Line));
        table.AddColumn("Check");
        table.AddColumn("State");
        table.AddRow("Configuration", "[#D7FF3F]Found[/]");
        table.AddRow("ADB", initial.AdbPath.Length > 0 ? "[#D7FF3F]Found[/]" : "[#858D83]Needs setup[/]");
        table.AddRow("scrcpy", initial.ScrcpyPath.Length > 0 ? "[#D7FF3F]Found[/]" : "[#858D83]Needs setup[/]");
        table.AddRow("Windows startup", initial.AutostartEnabled ? "[#D7FF3F]Enabled[/]" : "[#858D83]Not configured[/]");
        _console.Write(RexBrand.Panel("SETUP CHECK", table));

        _console.MarkupLine(
            $"[{RexBrand.Muted}]REX can install/verify scrcpy + ADB, configure startup, discover Android devices and set sane defaults. No administrator rights are requested.[/]");

        if (!_console.Confirm("Run guided setup now?", true))
            return false;

        await RunScriptAsync("Installing and verifying Android tools", _paths.Setup, new[] { "-SkipAutostart" });

        var enableAutostart = _console.Confirm(
            "Start Android Headless Mirror automatically when you sign in to Windows?",
            true);
        await SetAutostartAsync(enableAutostart);

        var devices = await WaitForDevicesAsync();
        var authorized = devices.Where(x => x.State == "device").ToArray();

        if (authorized.Length > 0)
        {
            var device = ChooseDevice(authorized, "Choose the Android device to configure now");
            await ConfigureLockScreenAsync(device);
        }
        else
        {
            RexBrand.Warn(_console, 
                "No authorized Android device is available yet. You can finish setup now; when you connect one, approve USB debugging once and REX will detect it.");
        }

        var turnOff = _console.Confirm(
            "Keep the physical phone display off while the PC mirror stays active?",
            true);
        _config.Set("TurnPhysicalScreenOff", turnOff.ToString().ToLowerInvariant());

        var stayAwake = _console.Confirm(
            "Keep Android awake while USB power is connected?",
            true);
        _config.Set("StayAwakeWhenUsb", stayAwake.ToString().ToLowerInvariant());

        if (_precisionTouchpadEligible)
        {
            var touchpad = _console.Confirm(
                "Enable Windows 11 Precision Touchpad gestures when supported?",
                true);
            _config.Set("MirrorChrome.NativeTouchpadGestures", touchpad.ToString().ToLowerInvariant());
        }

        var openControls = _console.Confirm(
            "Open the GUI Control Center automatically with each mirror?",
            false);
        _config.Set("ControlCenter.OpenOnLaunch", openControls.ToString().ToLowerInvariant());

        RexBrand.Success(_console, "Guided setup is complete.");

        if (_console.Confirm("Start Android Headless Mirror now?", true))
            await StartAsync();

        Pause();
        return true;
    }

    private async Task<RexStatus> GetStatusWithSpinnerAsync()
    {
        RexStatus? status = null;
        await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(RexBrand.Signal))
            .StartAsync("Checking Android Headless Mirror...", async _ =>
            {
                status = await _bridge.GetStatusAsync();
            });

        return status ?? throw new InvalidOperationException("Could not read package status.");
    }

    private void RenderStatus(RexStatus status)
    {
        var device = status.Devices.FirstOrDefault(x => x.State == "device");
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap());
        grid.AddColumn();

        grid.AddRow("[#858D83]SERVICE[/]", status.PersistentOff ? "[#FF774D]OFF[/]" : "[#D7FF3F]ON[/]");
        grid.AddRow("[#858D83]SUPERVISOR[/]", RexBrand.State(status.SupervisorRunning, "running", "stopped"));
        grid.AddRow("[#858D83]MIRROR[/]", RexBrand.State(status.MirrorRunning, "running", "waiting"));
        grid.AddRow("[#858D83]AUTOSTART[/]", RexBrand.State(status.AutostartEnabled, "enabled", "disabled"));

        if (device is null)
            grid.AddRow("[#858D83]DEVICE[/]", "[#858D83]No authorized device[/]");
        else
            grid.AddRow("[#858D83]DEVICE[/]", Markup.Escape(RexBrand.DeviceLabel(device)));

        _console.Write(RexBrand.Panel("STATUS", grid));
    }

    private async Task StartAsync()
    {
        if (File.Exists(_paths.StopFlag))
            File.Delete(_paths.StopFlag);

        await _runner.OpenAsync(_paths.StartBatch, _paths.Root);
        RexBrand.Success(_console, "Supervisor start requested. Connect any authorized Android device and the mirror will open automatically.");
        Pause();
    }

    private async Task StopAsync()
    {
        await RunScriptAsync("Stopping Android Headless Mirror", _paths.Stop);
        RexBrand.Success(_console, "Persistent OFF is active. Startup will not resurrect the mirror until you start it again.");
        Pause();
    }

    private async Task RuntimeControlsAsync()
    {
        var device = await ChooseAuthorizedDeviceAsync();
        if (device is null) return;

        var actions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sleep phone"] = "sleep",
            ["Wake screen"] = "wake",
            ["Power"] = "power",
            ["Home"] = "home",
            ["Back"] = "back",
            ["Recent apps"] = "apps",
            ["Menu / unlock"] = "menu",
            ["Rotate Android app"] = "rotate-device",
            ["Rotate mirror left"] = "rotate-left",
            ["Rotate mirror right"] = "rotate-right",
            ["Flip mirror horizontally"] = "flip-horizontal",
            ["Flip mirror vertically"] = "flip-vertical",
            ["Fullscreen"] = "fullscreen",
            ["Fit window"] = "fit",
            ["Pixel perfect"] = "pixel-perfect",
            ["Pause display"] = "pause",
            ["Resume display"] = "resume",
            ["Reset capture"] = "reset-capture",
            ["FPS counter"] = "fps",
            ["Volume up"] = "volume-up",
            ["Volume down"] = "volume-down",
            ["Notifications"] = "notifications",
            ["Quick settings"] = "quick-settings",
            ["Collapse panels"] = "collapse-panels",
            ["Copy from Android"] = "copy",
            ["Cut"] = "cut",
            ["Sync + paste"] = "paste-sync",
            ["Inject PC clipboard"] = "paste-inject",
            ["Keyboard settings"] = "keyboard-settings",
            ["Host zoom in"] = "mirror:zoom-in",
            ["Host zoom out"] = "mirror:zoom-out",
            ["Reset host zoom"] = "mirror:reset-zoom",
        };

        while (true)
        {
            RexBrand.Header(_console, $"CONTROLS · {device.DisplayName}");
            var choice = _console.Prompt(RexBrand.Menu(
                "Runtime mirror action",
                actions.Keys.Concat(new[] { "Back" })));

            if (choice == "Back") return;

            var actionValue = actions[choice];
            var bridgeAction = actionValue.StartsWith("mirror:", StringComparison.Ordinal)
                ? "mirror-command"
                : "scrcpy-action";
            var actionName = bridgeAction == "mirror-command"
                ? actionValue["mirror:".Length..]
                : actionValue;

            using var result = await _bridge.InvokeAsync(
                bridgeAction,
                serial: device.Serial,
                name: actionName);

            RexBrand.Success(_console, $"Sent {choice}.");
            await Task.Delay(180);
        }
    }

    private async Task PcSettingsAsync()
    {
        while (true)
        {
            RexBrand.Header(_console, "PC / MIRROR SETTINGS");
            var choice = _console.Prompt(RexBrand.Menu(
                "Settings",
                new[]
                {
                    "Common mirror settings",
                    "Video / audio / recording",
                    "Touchpad / host zoom",
                    "Pattern guide",
                    "Control Center",
                    "Wireless ADB",
                    "All settings browser",
                    "Back",
                }));

            if (choice == "Back") return;

            if (choice == "All settings browser")
            {
                await AllConfigSettingsAsync();
                continue;
            }

            var prefixes = choice switch
            {
                "Common mirror settings" => new[] { "TurnPhysicalScreenOff", "StayAwakeWhenUsb", "KeepActiveDuringMirror", "DismissKeyguardWhenPossible", "PreferUsb", "RestartOnUnexpectedExit", "PollSeconds", "RetrySeconds" },
                "Video / audio / recording" => new[] { "MaxSize", "MaxFps", "VideoBitRate", "ScrcpySession." },
                "Touchpad / host zoom" => new[] { "MirrorChrome." },
                "Pattern guide" => new[] { "PatternOverlay." },
                "Control Center" => new[] { "ControlCenter." },
                "Wireless ADB" => new[] { "Wireless." },
                _ => Array.Empty<string>(),
            };

            await EditConfigRowsAsync(_config.Flatten().Where(x => prefixes.Any(p =>
                p.EndsWith('.') ? x.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase) : x.Path.Equals(p, StringComparison.OrdinalIgnoreCase))).ToArray());
        }
    }

    private Task AllConfigSettingsAsync() => EditConfigRowsAsync(_config.Flatten());

    private async Task EditConfigRowsAsync(IReadOnlyList<ConfigLeaf> rows)
    {
        if (rows.Count == 0)
        {
            RexBrand.Warn(_console, "No settings matched this category.");
            Pause();
            return;
        }

        while (true)
        {
            RexBrand.Header(_console, "SETTINGS");
            var labels = rows.Select(x => $"{x.Path} = {x.Value}").ToArray();
            var choice = _console.Prompt(RexBrand.Menu(
                "Choose a setting",
                labels.Concat(new[] { "Back" })));

            if (choice == "Back") return;

            var index = Array.IndexOf(labels, choice);
            var leaf = rows[index];
            var current = _config.Get(leaf.Path);

            var value = _console.Ask(
                $"New value for [#D7FF3F]{Markup.Escape(leaf.Path)}[/] ([#858D83]{Markup.Escape(current.Value)}[/]):",
                current.Value);

            try
            {
                _config.Set(leaf.Path, value);
                RexBrand.Success(_console, $"{leaf.Path} saved.");
                rows = _config.Flatten().Where(x => rows.Any(old => old.Path == x.Path)).ToArray();
            }
            catch (Exception ex)
            {
                RexBrand.Error(_console, ex.Message);
                Pause();
            }

            await Task.Yield();
        }
    }

    private async Task DeviceSettingsAsync()
    {
        var device = await ChooseAuthorizedDeviceAsync();
        if (device is null) return;

        while (true)
        {
            RexBrand.Header(_console, $"DEVICE SETTINGS · {device.DisplayName}");
            var choice = _console.Prompt(RexBrand.Menu(
                "Android setting",
                new[]
                {
                    "Brightness",
                    "Brightness mode",
                    "Screen timeout",
                    "Auto rotate",
                    "Fixed rotation",
                    "Font scale",
                    "Show physical touches",
                    "Stay awake while plugged in",
                    "Animation speed",
                    "Dark mode",
                    "Wi-Fi",
                    "Mobile data",
                    "Airplane mode",
                    "Display size override",
                    "Display density override",
                    "Lock-screen mode",
                    "Save screenshot",
                    "Back",
                }));

            if (choice == "Back") return;
            if (choice == "Lock-screen mode")
            {
                await ConfigureLockScreenAsync(device);
                continue;
            }
            if (choice == "Save screenshot")
            {
                using var shot = await _bridge.InvokeAsync("screenshot", serial: device.Serial);
                RexBrand.Success(_console, shot.RootElement.TryGetProperty("Path", out var p) ? p.GetString() ?? "Screenshot saved." : "Screenshot saved.");
                Pause();
                continue;
            }

            var (id, value) = PromptFriendlySetting(choice);

            if (choice == "Animation speed")
            {
                foreach (var animationId in new[] { "animation-window", "animation-transition", "animation-duration" })
                {
                    using var _ = await _bridge.InvokeAsync(
                        "friendly-set",
                        serial: device.Serial,
                        name: animationId,
                        value: value);
                }

                RexBrand.Success(_console, "All Android animation scales updated.");
                Pause();
                continue;
            }

            using var result = await _bridge.InvokeAsync(
                "friendly-set",
                serial: device.Serial,
                name: id,
                value: value);

            var text = result.RootElement.TryGetProperty("Text", out var node)
                ? node.GetString() ?? "Updated."
                : "Updated.";
            RexBrand.Success(_console, text);
            Pause();
        }
    }

    private (string Id, string Value) PromptFriendlySetting(string choice)
    {
        return choice switch
        {
            "Brightness" => ("brightness", _console.Ask("Brightness 1-255:", 128).ToString()),
            "Brightness mode" => ("brightness-mode", Pick("Mode", ("Manual", "0"), ("Automatic", "1"))),
            "Screen timeout" => ("screen-timeout-ms", Pick("Timeout", ("15 seconds", "15000"), ("30 seconds", "30000"), ("1 minute", "60000"), ("2 minutes", "120000"), ("5 minutes", "300000"), ("10 minutes", "600000"), ("30 minutes", "1800000"))),
            "Auto rotate" => ("auto-rotate", _console.Confirm("Enable auto rotate?", true) ? "1" : "0"),
            "Fixed rotation" => ("user-rotation", Pick("Rotation", ("0 degrees", "0"), ("90 degrees", "1"), ("180 degrees", "2"), ("270 degrees", "3"))),
            "Font scale" => ("font-scale", Pick("Font scale", ("Small 0.85x", "0.85"), ("Default 1.0x", "1.0"), ("Large 1.15x", "1.15"), ("Extra large 1.30x", "1.30"))),
            "Show physical touches" => ("show-touches", _console.Confirm("Show Android touch indicators?") ? "1" : "0"),
            "Stay awake while plugged in" => ("stay-awake", _console.Confirm("Stay awake on all charging sources?", true) ? "7" : "0"),
            "Animation speed" => ("animation-window", Pick("Animation scale", ("Off", "0"), ("Fast 0.5x", "0.5"), ("Default 1x", "1"), ("Slow 1.5x", "1.5"))),
            "Dark mode" => ("dark-mode", Pick("Theme", ("Automatic", "auto"), ("Light", "no"), ("Dark", "yes"))),
            "Wi-Fi" => ("wifi", Pick("Wi-Fi", ("Enable", "enable"), ("Disable", "disable"))),
            "Mobile data" => ("mobile-data", Pick("Mobile data", ("Enable", "enable"), ("Disable", "disable"))),
            "Airplane mode" => ("airplane-mode", Pick("Airplane mode", ("Enable", "enable"), ("Disable", "disable"))),
            "Display size override" => ("wm-size", _console.Ask("Size (for example 1080x2400, or reset):", "reset")),
            "Display density override" => ("wm-density", _console.Ask("Density 120-1000, or reset:", "reset")),
            _ => throw new InvalidOperationException($"Unknown setting '{choice}'."),
        };
    }

    private async Task AdvancedAndroidAsync()
    {
        var device = await ChooseAuthorizedDeviceAsync();
        if (device is null) return;

        while (true)
        {
            RexBrand.Header(_console, $"ADVANCED ANDROID · {device.DisplayName}");
            var ns = _console.Prompt(RexBrand.Menu(
                "Settings Provider namespace",
                new[] { "system", "secure", "global", "Back" }));
            if (ns == "Back") return;

            var rows = await _bridge.ListAndroidSettingsAsync(device.Serial, ns);
            var filter = _console.Ask("Filter key/value (leave blank for all):", string.Empty);
            var filtered = rows
                .Where(x => string.IsNullOrWhiteSpace(filter) ||
                            x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(250)
                .ToArray();

            if (filtered.Length == 0)
            {
                RexBrand.Warn(_console, "No settings matched.");
                Pause();
                continue;
            }

            var labels = filtered.Select(x => $"{x.Key} = {x.Value} [{x.Risk}]").ToArray();
            var selected = _console.Prompt(RexBrand.Menu(
                $"{ns} settings",
                labels.Concat(new[] { "Back" })));
            if (selected == "Back") continue;

            var row = filtered[Array.IndexOf(labels, selected)];
            var action = _console.Prompt(RexBrand.Menu(
                row.Key,
                new[] { "Change value", "Delete key", "Back" }));
            if (action == "Back") continue;

            if (row.Risk == "protected")
            {
                RexBrand.Warn(_console, "REX protects this key because changing it can break ADB recovery or device identity.");
                Pause();
                continue;
            }

            if (!_console.Confirm($"Apply {action.ToLowerInvariant()} to {ns}/{row.Key}?", false))
                continue;

            if (action == "Change value")
            {
                var newValue = _console.Ask("New value:", row.Value);
                using var result = await _bridge.InvokeAsync(
                    "settings-set",
                    serial: device.Serial,
                    ns: ns,
                    key: row.Key,
                    value: newValue);
                RexBrand.Success(_console, "Android setting write completed.");
            }
            else
            {
                using var result = await _bridge.InvokeAsync(
                    "settings-delete",
                    serial: device.Serial,
                    ns: ns,
                    key: row.Key);
                RexBrand.Success(_console, "Android setting delete completed.");
            }

            Pause();
        }
    }

    private async Task OpenControlCenterAsync()
    {
        var device = await ChooseAuthorizedDeviceAsync();
        if (device is null) return;

        var args = new[]
        {
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
            "-File", _paths.ControlCenter,
            "-Serial", device.Serial,
        };

        _ = await _runner.StartDetachedAsync(
            "powershell.exe",
            args,
            _paths.Root);

        RexBrand.Success(_console, $"Control Center opened for {device.DisplayName}.");
    }

    private async Task DiagnosticsAsync()
    {
        RexBrand.Header(_console, "DIAGNOSTICS");
        var result = await _runner.RunPowerShellAsync(_paths.Diagnostics);
        var body = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        _console.Write(RexBrand.Panel("SYSTEM REPORT", new Text(body.Trim())));
        Pause();
    }

    private async Task RepairAsync(RexStatus before)
    {
        var autostart = before.AutostartEnabled;
        await RunScriptAsync("Verifying / repairing scrcpy and ADB", _paths.Setup, new[] { "-SkipAutostart" });
        await SetAutostartAsync(autostart);
        RexBrand.Success(_console, "Repair completed without changing your startup preference.");
        Pause();
    }

    private async Task AutostartAsync()
    {
        var status = await _bridge.GetStatusAsync();
        var choice = _console.Prompt(RexBrand.Menu(
            $"Windows startup is currently {(status.AutostartEnabled ? "enabled" : "disabled")}",
            new[] { "Enable", "Disable", "Back" }));

        if (choice == "Back") return;
        await SetAutostartAsync(choice == "Enable");
        Pause();
    }

    private async Task SetAutostartAsync(bool enabled)
    {
        await RunScriptAsync(
            enabled ? "Enabling Windows startup" : "Disabling Windows startup",
            enabled ? _paths.InstallAutostart : _paths.RemoveAutostart);
        RexBrand.Success(_console, enabled ? "Windows startup enabled." : "Windows startup disabled.");
    }

    private async Task CapturesAsync()
    {
        var choice = _console.Prompt(RexBrand.Menu(
            "Captures",
            new[] { "Save screenshot now", "Open captures folder", "Back" }));

        if (choice == "Back") return;

        if (choice == "Open captures folder")
        {
            Directory.CreateDirectory(_paths.Captures);
            await _runner.OpenAsync(_paths.Captures);
            return;
        }

        var device = await ChooseAuthorizedDeviceAsync();
        if (device is null) return;

        using var shot = await _bridge.InvokeAsync("screenshot", serial: device.Serial);
        var path = shot.RootElement.TryGetProperty("Path", out var p)
            ? p.GetString() ?? "Screenshot saved."
            : "Screenshot saved.";
        RexBrand.Success(_console, path);
        Pause();
    }

    private async Task ConfigureLockScreenAsync(RexDevice device)
    {
        var choice = _console.Prompt(RexBrand.Menu(
            $"Unlock method for {device.DisplayName}",
            new[]
            {
                "Pattern",
                "PIN / password / biometric / other",
                "No screen lock",
                "Ask automatically later",
            }));

        if (choice == "Ask automatically later")
        {
            await RunScriptAsync(
                "Clearing saved lock-screen choice",
                _paths.ResetLockChoices,
                new[] { "-Serial", device.Serial });
            return;
        }

        var mode = choice switch
        {
            "Pattern" => "pattern",
            "PIN / password / biometric / other" => "other",
            _ => "none",
        };

        using var result = await _bridge.InvokeAsync(
            "set-lock-mode",
            serial: device.Serial,
            value: mode);
        RexBrand.Success(_console, $"Saved lock-screen mode: {mode}.");
    }

    private async Task<IReadOnlyList<RexDevice>> WaitForDevicesAsync()
    {
        var devices = await _bridge.GetDevicesAsync();
        if (devices.Count > 0) return devices;

        if (!_console.Confirm("No Android device is detected. Wait for a USB device now?", true))
            return devices;

        var deadline = DateTime.UtcNow.AddSeconds(45);
        await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(RexBrand.Signal))
            .StartAsync("Waiting for Android device...", async ctx =>
            {
                while (DateTime.UtcNow < deadline)
                {
                    devices = await _bridge.GetDevicesAsync();
                    if (devices.Count > 0) return;
                    await Task.Delay(1000);
                }
            });

        return devices;
    }

    private async Task<RexDevice?> ChooseAuthorizedDeviceAsync()
    {
        var devices = await _bridge.GetDevicesAsync();
        var authorized = devices.Where(x => x.State == "device").ToArray();

        if (authorized.Length == 0)
        {
            var unauthorized = devices.FirstOrDefault(x => x.State == "unauthorized");
            if (unauthorized is not null)
            {
                RexBrand.Warn(_console, 
                    $"Android device {unauthorized.Serial} is connected but not authorized. Unlock it once, approve 'Allow USB debugging', and select 'Always allow from this computer'.");
            }
            else
            {
                RexBrand.Warn(_console, "No authorized Android device is currently connected.");
            }
            Pause();
            return null;
        }

        return ChooseDevice(authorized, "Choose Android device");
    }

    private RexDevice ChooseDevice(IReadOnlyList<RexDevice> devices, string title)
    {
        if (devices.Count == 1) return devices[0];

        var labels = devices.Select(RexBrand.DeviceLabel).ToArray();
        var selected = _console.Prompt(RexBrand.Menu(title, labels));
        return devices[Array.IndexOf(labels, selected)];
    }

    private async Task RunScriptAsync(
        string label,
        string script,
        IEnumerable<string>? args = null)
    {
        ProcessResult? result = null;

        await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(RexBrand.Signal))
            .StartAsync(label + "...", async _ =>
            {
                result = await _runner.RunPowerShellAsync(script, args);
            });

        if (result is null)
            throw new InvalidOperationException($"{label} did not produce a result.");

        if (!result.Ok)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException(error.Trim());
        }
    }

    private string Pick(string title, params (string Label, string Value)[] options)
    {
        var choice = _console.Prompt(RexBrand.Menu(title, options.Select(x => x.Label)));
        return options.First(x => x.Label == choice).Value;
    }

    private void Pause()
    {
        if (!_pauseEnabled) return;
        _console.MarkupLine($"[{RexBrand.Muted}]Press Enter to continue...[/]");
        Console.ReadLine();
    }
}
