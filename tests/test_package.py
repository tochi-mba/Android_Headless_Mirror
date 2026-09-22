import json
import re
import unittest
from collections import Counter
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]

REX_COLORS = {
    "--ink": "#080A09",
    "--panel": "#111512",
    "--raised": "#181E19",
    "--line": "#29302A",
    "--text": "#F2F5EE",
    "--muted": "#858D83",
    "--signal": "#D7FF3F",
    "--live": "#FF774D",
}


def parse_adb_devices(text):
    out = []
    for line in text.splitlines():
        match = re.match(
            r"^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)",
            line,
        )
        if match:
            serial, state = match.groups()
            out.append(
                {
                    "serial": serial,
                    "state": state,
                    "is_tcp": bool(re.search(r":\d+$", serial)),
                }
            )
    return out


def select_device(devices, preferred="", prefer_usb=True):
    ready = [device for device in devices if device["state"] == "device"]
    if not ready:
        return None

    if preferred:
        for device in ready:
            if device["serial"] == preferred:
                return device

    if prefer_usb:
        usb = [device for device in ready if not device["is_tcp"]]
        if usb:
            return usb[0]

    return ready[0]


def is_private_ipv4(ip):
    try:
        nums = [int(part) for part in ip.split(".")]
    except ValueError:
        return False

    if len(nums) != 4 or any(part < 0 or part > 255 for part in nums):
        return False

    a, b, _, _ = nums
    return (
        a == 10
        or (a == 192 and b == 168)
        or (a == 172 and 16 <= b <= 31)
    )


def phone_ip_candidates(text):
    preferred = []
    fallback = []

    for line in text.splitlines():
        match = re.match(
            r"^\d+:\s+([^:\s]+).*?\sinet\s+(\d+\.\d+\.\d+\.\d+)/",
            line,
        )
        if not match:
            continue

        iface, ip = match.groups()
        if not is_private_ipv4(ip) or ip == "127.0.0.1":
            continue

        if ip not in fallback:
            fallback.append(ip)

        if re.search(r"(wlan|swlan|ap|softap|wifi)", iface, re.I):
            if ip not in preferred:
                preferred.append(ip)

    return preferred or fallback


class PageScanner(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = []
        self.links = []
        self.assets = []
        self.buttons = []
        self.tags = Counter()
        self.inline_handlers = []
        self.meta = {}
        self.title = ""
        self._in_title = False

    def handle_starttag(self, tag, attrs):
        self.tags[tag] += 1
        attributes = dict(attrs)

        if "id" in attributes:
            self.ids.append(attributes["id"])

        if tag == "a" and "href" in attributes:
            self.links.append(attributes["href"])

        if tag in {"link", "script", "img"}:
            value = attributes.get("href") or attributes.get("src")
            if value:
                self.assets.append(value)

        if tag == "button":
            self.buttons.append(attributes)

        if tag == "meta":
            key = (
                attributes.get("name")
                or attributes.get("property")
                or attributes.get("http-equiv")
            )
            if key:
                self.meta[key] = attributes.get("content", "")

        if tag == "title":
            self._in_title = True

        for key in attributes:
            if key.lower().startswith("on"):
                self.inline_handlers.append((tag, key))

    def handle_endtag(self, tag):
        if tag == "title":
            self._in_title = False

    def handle_data(self, data):
        if self._in_title:
            self.title += data


class RepositoryContractTests(unittest.TestCase):
    maxDiff = None

    def read(self, path):
        return (ROOT / path).read_text(encoding="utf-8-sig")

    def scan_page(self):
        parser = PageScanner()
        parser.feed(self.read("docs/index.html"))
        return parser

    # ---------- Repository/package shape ----------

    def test_required_package_files_exist(self):
        required = [
            "SETUP_AND_START.bat",
            "START_NOW.bat",
            "START_WITH_LOG.bat",
            "STOP.bat",
            "DIAGNOSTICS.bat",
            "REMOVE_AUTOSTART.bat",
            "REX.bat",
            "Bootstrap-RexCli.ps1",
            "Install-Rex-Shortcut.ps1",
            "Remove-Rex-Shortcut.ps1",
            "RexBridge.ps1",
            "Setup.ps1",
            "Install-Autostart.ps1",
            "Remove-Autostart.ps1",
            "Start-PhoneMirror.ps1",
            "Stop-PhoneMirror.ps1",
            "PatternOverlay.ps1",
            "MirrorChrome.ps1",
            "ControlCenter.ps1",
            "ControlCenter.xaml",
            "DeviceControl.ps1",
            "ScrcpyControl.ps1",
            "Reset-LockScreenChoices.ps1",
            "RESET_LOCK_SCREEN_CHOICES.bat",
            "Start-Hidden.vbs",
            "config.json",
            "README.md",
            "docs/DISPLAY_TRANSPORTS.md",
            "docs/DISPLAY_COMPATIBILITY.md",
            "docs/ROOT_ARCHITECTURE.md",
            "docs/ROOT_SECURITY.md",
            "docs/ROOT_COMPATIBILITY.md",
            ".gitignore",
            ".github/workflows/ci.yml",
            ".github/workflows/pages.yml",
            ".github/workflows/release.yml",
            "src/Rex.AndroidMirror.Cli/Rex.AndroidMirror.Cli.csproj",
            "src/Rex.AndroidMirror.Cli/Program.cs",
            "src/Rex.AndroidMirror.Cli/RexApp.cs",
            "src/Rex.AndroidMirror.Cli/RexBrand.cs",
            "src/Rex.AndroidMirror.Cli/ConfigStore.cs",
            "src/Rex.AndroidMirror.Cli/BridgeClient.cs",
            "src/Rex.AndroidMirror.Cli/MachineMode.cs",
            "src/Rex.AndroidMirror.Cli/SmartLaunch.cs",
            "src/Rex.AndroidMirror.Cli/DisplayModels.cs",
            "src/Rex.AndroidMirror.Cli/DisplayManager.cs",
            "src/Rex.AndroidMirror.Cli/WindowsDisplayHostProbe.cs",
            "src/Rex.AndroidMirror.Cli/DisplayVerificationStore.cs",
            "src/Rex.AndroidMirror.Cli/AndroidShellRunner.cs",
            "src/Rex.AndroidMirror.Cli/RootModels.cs",
            "src/Rex.AndroidMirror.Cli/RootPolicy.cs",
            "src/Rex.AndroidMirror.Cli/RootStateStore.cs",
            "src/Rex.AndroidMirror.Cli/RootManager.cs",
            "src/Rex.AndroidMirror.Cli/RootFeatureService.cs",
            "src/Rex.AndroidMirror.Cli/RootCommandRouter.cs",
            "src/Rex.AndroidMirror.Cli/DeviceCapabilityRegistry.cs",
            "AGENTS.md",
            "tests/Rex.AndroidMirror.Cli.Tests/Rex.AndroidMirror.Cli.Tests.csproj",
            "tests/Test-RexCliBootstrap.ps1",
            "tests/Test-RexShortcut.ps1",
            "tests/Test-RexBridgeE2E.ps1",
            "tests/Test-PowerShellBehavior.ps1",
            "tests/Test-ControlCenterE2E.ps1",
            "tests/Test-ControlCenterVisual.ps1",
            "tests/visual-thresholds.json",
            "tests/pages.spec.js",
            "playwright.config.js",
            "package.json",
            "global.json",
        ]
        for path in required:
            with self.subTest(path=path):
                self.assertTrue((ROOT / path).is_file(), path)

    def test_legacy_stop_scripts_are_absent(self):
        for path in [
            "HARD_STOP.ps1",
            "HARD_STOP.bat",
            "HARD_STOP_AND_DISABLE_AUTOSTART.bat",
        ]:
            with self.subTest(path=path):
                self.assertFalse((ROOT / path).exists(), path)

    def test_no_hardcoded_user_profile_paths(self):
        patterns = [
            r"C:\\Users\\",
            r"C:/Users/",
            r"\\Users\\tochi",
            r"/home/",
        ]
        checked = [
            *ROOT.glob("*.ps1"),
            *ROOT.glob("*.bat"),
            *ROOT.glob("*.vbs"),
            ROOT / "config.json",
        ]
        for path in checked:
            text = path.read_text(encoding="utf-8-sig")
            for pattern in patterns:
                with self.subTest(path=path.name, pattern=pattern):
                    self.assertIsNone(re.search(pattern, text, re.I))

    def test_runtime_artifacts_are_gitignored(self):
        gitignore = self.read(".gitignore")
        for entry in ["tools/", "logs/", "state.json", "stop.flag", "*.log", "pattern-calibration/", "runtime/", "captures/", "test-results/", "artifacts/", "display-verification.json", "display-verification.json.tmp", "root-state.json", "root-state.json.tmp", "config.json.rex-backup", "config.json.tmp", "config.json.restore-current"]:
            self.assertIn(entry, gitignore)

    # ---------- Configuration ----------

    def test_config_schema_is_expected(self):
        config = json.loads(self.read("config.json"))
        self.assertEqual(
            set(config),
            {
                "WindowTitle",
                "PollSeconds",
                "RetrySeconds",
                "WakeBeforeMirror",
                "TurnPhysicalScreenOff",
                "StayAwakeWhenUsb",
                "KeepActiveDuringMirror",
                "DismissKeyguardWhenPossible",
                "PowerOffOnClose",
                "MaxSize",
                "MaxFps",
                "VideoBitRate",
                "PreferUsb",
                "RestartOnUnexpectedExit",
                "PreferredSerial",
                "Wireless",
                "Logging",
                "PatternOverlay",
                "MirrorChrome",
                "ControlCenter",
                "ScrcpySession",
                "Display",
                "Root",
                "ExtraScrcpyArgs",
            },
        )
        self.assertEqual(
            set(config["Wireless"]),
            {
                "Enabled",
                "Port",
                "EnableTcpipWhenUsbAvailable",
                "TrySavedAddresses",
                "TryWindowsDefaultGateway",
                "ManualHosts",
            },
        )
        self.assertEqual(
            set(config["Logging"]),
            {"Enabled", "MaxBytes", "KeepFiles"},
        )
        self.assertEqual(
            set(config["PatternOverlay"]),
            {
                "Enabled",
                "PromptPerDevice",
                "AutoShowOnKeyguard",
                "ManualToggleHotkey",
                "KeyguardPollMilliseconds",
                "WindowPollMilliseconds",
                "Opacity",
                "GridCenterX",
                "GridCenterY",
                "GridSizeRelativeToWidth",
                "DotRadiusRelativeToWidth",
                "ShowCursorTrail",
                "ManualShowSeconds",
                "TrailHoldMilliseconds",
                "FrameMilliseconds",
                "AutoDiscoverGeometry",
                "DiscoveryPollMilliseconds",
                "CalibrationEnabled",
                "CalibrationHotkey",
                "CalibrationStepPixels",
                "CalibrationFineStepPixels",
                "CalibrationDirectory",
                "FallbackToEstimatedGeometry",
            },
        )
        self.assertEqual(
            set(config["MirrorChrome"]),
            {
                "Enabled",
                "SleepButton",
                "HostZoomEnabled",
                "ZoomStep",
                "MinZoom",
                "MaxZoom",
                "ToolbarInsetPixels",
                "PollMilliseconds",
                "CtrlWheelZoom",
                "NativeTouchpadGestures",
                "TouchpadPinchToAndroid",
                "CtrlTouchpadPinchToHostZoom",
                "TouchpadPinchThreshold",
                "TouchpadBaseRadiusRelativeToClient",
            },
        )
        self.assertEqual(
            set(config["ControlCenter"]),
            {
                "Enabled",
                "OpenOnLaunch",
                "DockToMirror",
                "AlwaysOnTop",
                "Width",
                "Height",
                "RememberLastTab",
                "ConfirmSensitiveDeviceWrites",
                "AdvancedSettingsWritesEnabled",
                "ScreenshotDirectory",
            },
        )
        self.assertEqual(
            set(config["ScrcpySession"]),
            {
                "VideoCodec",
                "AudioEnabled",
                "AudioCodec",
                "AudioDup",
                "AudioBufferMs",
                "Fullscreen",
                "AlwaysOnTop",
                "DisableScreensaver",
                "RecordOnStart",
                "RecordDirectory",
            },
        )
        self.assertEqual(
            set(config["Display"]),
            {
                "DefaultTransport",
                "ProtectedContentPolicy",
                "WindowsWirelessDisplay",
                "SamsungDex",
            },
        )
        self.assertEqual(
            set(config["Display"]["WindowsWirelessDisplay"]),
            {"Enabled", "AutoOpenReceiver"},
        )
        self.assertEqual(
            set(config["Display"]["SamsungDex"]),
            {"Enabled"},
        )
        self.assertEqual(
            set(config["Root"]),
            {
                "Enabled",
                "ProbeOnConnect",
                "RequestAutomatically",
                "AllowReadOnly",
                "AllowReversible",
                "AllowSystemChanges",
                "AllowDeviceCritical",
                "RawShellEnabled",
                "RequestTimeoutSeconds",
                "CommandTimeoutSeconds",
                "MaxOutputCharacters",
            },
        )

    def test_config_defaults_are_safe_and_bounded(self):
        config = json.loads(self.read("config.json"))
        self.assertTrue(config["PreferUsb"])
        self.assertTrue(config["StayAwakeWhenUsb"])
        self.assertTrue(config["KeepActiveDuringMirror"])
        self.assertTrue(config["DismissKeyguardWhenPossible"])
        self.assertEqual(config["PollSeconds"], 1)
        self.assertFalse(config["Wireless"]["Enabled"])
        self.assertFalse(config["Wireless"]["EnableTcpipWhenUsbAvailable"])
        self.assertEqual(config["Wireless"]["Port"], 5555)
        self.assertGreaterEqual(config["PollSeconds"], 1)
        self.assertLessEqual(config["PollSeconds"], 30)
        self.assertGreaterEqual(config["RetrySeconds"], 1)
        self.assertLessEqual(config["RetrySeconds"], 60)
        self.assertGreater(config["Logging"]["MaxBytes"], 100_000)
        self.assertGreaterEqual(config["Logging"]["KeepFiles"], 1)
        self.assertEqual(config["WindowTitle"], "Android Device")
        self.assertEqual(config["Display"]["DefaultTransport"], "scrcpy")
        self.assertEqual(config["Display"]["ProtectedContentPolicy"], "prompt")
        self.assertTrue(config["Display"]["WindowsWirelessDisplay"]["Enabled"])
        self.assertTrue(config["Display"]["SamsungDex"]["Enabled"])

        root = config["Root"]
        self.assertTrue(root["Enabled"])
        self.assertTrue(root["ProbeOnConnect"])
        self.assertFalse(root["RequestAutomatically"])
        self.assertTrue(root["AllowReadOnly"])
        self.assertFalse(root["AllowReversible"])
        self.assertFalse(root["AllowSystemChanges"])
        self.assertFalse(root["AllowDeviceCritical"])
        self.assertFalse(root["RawShellEnabled"])
        self.assertGreaterEqual(root["RequestTimeoutSeconds"], 3)
        self.assertLessEqual(root["RequestTimeoutSeconds"], 60)
        self.assertGreaterEqual(root["CommandTimeoutSeconds"], 1)
        self.assertLessEqual(root["CommandTimeoutSeconds"], 120)
        self.assertGreaterEqual(root["MaxOutputCharacters"], 4096)
        self.assertLessEqual(root["MaxOutputCharacters"], 4_194_304)

        root = config["Root"]
        self.assertTrue(root["Enabled"])
        self.assertTrue(root["ProbeOnConnect"])
        self.assertFalse(root["RequestAutomatically"])
        self.assertTrue(root["AllowReadOnly"])
        self.assertFalse(root["AllowReversible"])
        self.assertFalse(root["AllowSystemChanges"])
        self.assertFalse(root["AllowDeviceCritical"])
        self.assertFalse(root["RawShellEnabled"])
        self.assertGreaterEqual(root["RequestTimeoutSeconds"], 3)
        self.assertGreaterEqual(root["CommandTimeoutSeconds"], 1)
        self.assertGreaterEqual(root["MaxOutputCharacters"], 4096)

        overlay = config["PatternOverlay"]
        self.assertTrue(overlay["Enabled"])
        self.assertTrue(overlay["PromptPerDevice"])
        self.assertTrue(overlay["AutoShowOnKeyguard"])
        self.assertEqual(overlay["ManualToggleHotkey"], "Ctrl+Alt+P")
        self.assertGreaterEqual(overlay["Opacity"], 0.2)
        self.assertLessEqual(overlay["Opacity"], 1.0)
        self.assertGreater(overlay["GridSizeRelativeToWidth"], 0.0)
        self.assertLessEqual(overlay["GridSizeRelativeToWidth"], 1.0)
        self.assertGreater(overlay["DotRadiusRelativeToWidth"], 0.0)
        self.assertGreaterEqual(overlay["WindowPollMilliseconds"], 50)
        self.assertGreaterEqual(overlay["KeyguardPollMilliseconds"], 250)
        self.assertGreaterEqual(overlay["FrameMilliseconds"], 12)
        self.assertTrue(overlay["AutoDiscoverGeometry"])
        self.assertGreaterEqual(overlay["DiscoveryPollMilliseconds"], 1000)
        self.assertTrue(overlay["CalibrationEnabled"])
        self.assertEqual(overlay["CalibrationHotkey"], "Ctrl+Alt+C")
        self.assertGreater(overlay["CalibrationStepPixels"], 0)
        self.assertGreater(overlay["CalibrationFineStepPixels"], 0)
        self.assertEqual(overlay["CalibrationDirectory"], "pattern-calibration")
        self.assertTrue(overlay["FallbackToEstimatedGeometry"])

        chrome = config["MirrorChrome"]
        self.assertTrue(chrome["Enabled"])
        self.assertTrue(chrome["SleepButton"])
        self.assertTrue(chrome["HostZoomEnabled"])
        self.assertTrue(chrome["CtrlWheelZoom"])
        self.assertTrue(chrome["NativeTouchpadGestures"])
        self.assertTrue(chrome["TouchpadPinchToAndroid"])
        self.assertTrue(chrome["CtrlTouchpadPinchToHostZoom"])
        self.assertGreater(chrome["ZoomStep"], 0)
        self.assertGreaterEqual(chrome["MinZoom"], 1.0)
        self.assertGreater(chrome["MaxZoom"], chrome["MinZoom"])
        self.assertGreaterEqual(chrome["PollMilliseconds"], 12)
        self.assertGreaterEqual(chrome["ToolbarInsetPixels"], 0)
        self.assertGreater(chrome["TouchpadPinchThreshold"], 0)
        self.assertLess(chrome["TouchpadPinchThreshold"], 0.2)
        self.assertGreater(chrome["TouchpadBaseRadiusRelativeToClient"], 0.05)
        self.assertLess(chrome["TouchpadBaseRadiusRelativeToClient"], 0.5)

        center = config["ControlCenter"]
        self.assertTrue(center["Enabled"])
        self.assertGreaterEqual(center["Width"], 900)
        self.assertGreaterEqual(center["Height"], 640)
        self.assertTrue(center["ConfirmSensitiveDeviceWrites"])
        self.assertTrue(center["AdvancedSettingsWritesEnabled"])
        self.assertTrue(center["DockToMirror"])
        self.assertTrue(center["AlwaysOnTop"])
        self.assertTrue(center["RememberLastTab"])
        self.assertFalse(center["OpenOnLaunch"])
        self.assertTrue(center["ScreenshotDirectory"].startswith("captures/"))

        session = config["ScrcpySession"]
        self.assertIn(session["VideoCodec"], {"h264", "h265", "av1"})
        self.assertIn(session["AudioCodec"], {"opus", "aac", "flac", "raw"})
        self.assertTrue(session["AudioEnabled"])
        self.assertFalse(session["AudioDup"])
        self.assertGreaterEqual(session["AudioBufferMs"], 0)
        self.assertFalse(session["Fullscreen"])
        self.assertFalse(session["RecordOnStart"])
        self.assertTrue(session["DisableScreensaver"])
        self.assertTrue(session["RecordDirectory"].startswith("captures/"))
        self.assertEqual(config["ExtraScrcpyArgs"], "")

    # ---------- Pattern overlay ----------

    def test_lock_screen_modes_cover_pattern_other_none_and_session_off(self):
        text = self.read("Start-PhoneMirror.ps1")
        prompt_start = text.index("function Get-OrPromptLockScreenMode")
        prompt_end = text.index("function Find-Tool", prompt_start)
        prompt = text[prompt_start:prompt_end]
        for mode in ['"pattern"', '"other"', '"none"', '"session-off"']:
            self.assertIn(mode, prompt)
        self.assertIn("Does this Android device use any screen lock?", prompt)
        self.assertIn("Does this device use Android pattern unlock?", prompt)

    def test_only_pattern_mode_starts_overlay_sidecar(self):
        text = self.read("Start-PhoneMirror.ps1")
        start = text.index("function Start-PatternOverlay")
        end = text.index("function Stop-PatternOverlay", start)
        function = text[start:end]
        self.assertIn('$LockScreenMode -ne "pattern"', function)
        self.assertIn("PatternOverlay.ps1", function)

    def test_overlay_is_visual_only_and_never_injects_unlock_input(self):
        text = self.read("PatternOverlay.ps1").lower()
        forbidden = [
            "input swipe",
            "input tap",
            "input text",
            "locksettings verify",
            "locksettings set",
            "password=",
            "pin=",
        ]
        for needle in forbidden:
            with self.subTest(needle=needle):
                self.assertNotIn(needle, text)

    def test_overlay_persists_only_calibration_geometry_not_pattern_or_cursor_path(self):
        text = self.read("PatternOverlay.ps1")
        for writer in ["Add-Content", "Out-File", "Export-Csv"]:
            with self.subTest(writer=writer):
                self.assertNotIn(writer, text)

        self.assertNotIn("state.json", text)
        self.assertIn("$trail.Points.Clear()", text)
        self.assertIn("function Save-PatternCalibration", text)

        start = text.index("function Save-PatternCalibration")
        end = text.index("function Remove-PatternCalibration", start)
        save = text[start:end]
        for forbidden in ["$trail", "Points", "Cursor", "Path="]:
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, save)

        for field in ["Left", "Top", "Right", "Bottom", "Serial"]:
            self.assertIn(field, save)

    def test_overlay_window_is_click_through_and_non_activating(self):
        text = self.read("PatternOverlay.ps1")
        for contract in [
            "WS_EX_TRANSPARENT",
            "WS_EX_NOACTIVATE",
            "WS_EX_TOOLWINDOW",
            "SWP_NOACTIVATE",
            "IsHitTestVisible = $false",
            "ShowActivated = $false",
        ]:
            self.assertIn(contract, text)

    def test_overlay_supports_window_tracking_dpi_and_multi_monitor_coordinates(self):
        text = self.read("PatternOverlay.ps1")
        for contract in [
            "GetClientRect",
            "ClientToScreen",
            "SetWindowPos",
            "SetProcessDpiAwarenessContext",
            "FindWindowByExactTitle",
        ]:
            self.assertIn(contract, text)

    def test_overlay_has_keyguard_detection_and_manual_fallback(self):
        text = self.read("PatternOverlay.ps1")
        for contract in [
            "Get-KeyguardStateFromText",
            "dumpsys window",
            "dumpsys trust",
            "AutoShowOnKeyguard",
            "ManualToggleHotkey",
            "GetAsyncKeyState",
        ]:
            self.assertIn(contract, text)

    def test_overlay_discovers_runtime_pattern_geometry_before_estimate(self):
        text = self.read("PatternOverlay.ps1")
        for contract in [
            "uiautomator dump --compressed",
            "Get-PatternGeometryFromUiXml",
            "LockPatternView",
            "lockPatternView",
            '"ui-dots"',
            '"ui-view"',
            "Get-EffectivePatternGeometry",
        ]:
            self.assertIn(contract, text)

        effective_start = text.index("function Get-EffectivePatternGeometry")
        effective_end = text.index("function Get-GridBoundsNormalizedFromPoints", effective_start)
        effective = text[effective_start:effective_end]
        self.assertLess(effective.index('"ui-dots"'), effective.index("$script:calibrationGeometry"))
        self.assertLess(effective.index("$script:calibrationGeometry"), effective.rindex("$script:discoveredGeometry"))

    def test_overlay_calibration_is_per_device_and_keyboard_only(self):
        text = self.read("PatternOverlay.ps1")
        for contract in [
            "CalibrationHotkey",
            "Start-CalibrationMode",
            "Adjust-CalibrationDraft",
            "Save-PatternCalibration",
            "Get-CalibrationPath",
            "ARROWS MOVE",
            "SHIFT+ARROWS RESIZE",
            "ENTER SAVE",
            "ESC CANCEL",
            "R RESET",
        ]:
            self.assertIn(contract, text)

        self.assertIn("CalibrationDirectory", text)
        self.assertNotIn("input tap", text.lower())
        self.assertNotIn("input swipe", text.lower())

    def test_runtime_calibration_directory_is_gitignored(self):
        self.assertIn("pattern-calibration/", self.read(".gitignore"))

    def test_supervisor_migrates_old_state_shape_for_device_profiles(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("function Ensure-StateShape", text)
        self.assertIn('Add-Member -NotePropertyName DeviceProfiles', text)
        self.assertIn("Ensure-StateShape (Get-Content $StateFile", text)

    def test_stop_kills_overlay_sidecars_without_touching_global_adb(self):
        text = self.read("Stop-PhoneMirror.ps1")
        self.assertIn("PatternOverlay\\.ps1", text)
        self.assertIn("Stop-Process", text)
        self.assertNotIn("kill-server", text)
        self.assertNotIn("adb.exe", text)

    def test_lock_screen_choice_reset_also_clears_pattern_calibration(self):
        text = self.read("Reset-LockScreenChoices.ps1")
        self.assertIn("DeviceProfiles", text)
        self.assertIn("PreferredSerial", self.read("Start-PhoneMirror.ps1"))
        self.assertIn("CalibrationDirectory", text)
        self.assertIn("Remove-CalibrationForSerial", text)
        self.assertIn("Remove-AllCalibrations", text)
        self.assertIn('if ($Serial -eq "ALL")', text)

    # ---------- Mirror toolbar / multitouch / host zoom ----------

    def test_scrcpy_forces_sdk_mouse_mode_for_ctrl_drag_multitouch(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn('$args.Add("--mouse=sdk")', text)

    def test_native_precision_touchpad_pinch_is_primary_multitouch_path(self):
        text = self.read("MirrorChrome.ps1")
        for contract in [
            "RegisterPrecisionTouchpadWindow",
            "GetPointerTouchpadInfoDelegate",
            "WM_POINTERDOWN",
            "WM_POINTERUPDATE",
            "WM_POINTERUP",
            "ptHimetricLocation",
            "Get-TouchpadGestureMetrics",
            "BeginScrcpyPinch",
            "UpdateScrcpyPinch",
            "EndScrcpyPinch",
        ]:
            self.assertIn(contract, text)

        self.assertIn("new IntPtr(2689)", text)
        self.assertIn("new IntPtr(2691)", text)

    def test_touchpad_pinch_needs_no_keyboard_modifier_for_android(self):
        text = self.read("MirrorChrome.ps1")
        start = text.index("function Update-TouchpadGesture")
        end = text.index("$script:gestureHook", start)
        function = text[start:end]

        ctrl_check = function.index("$ctrlPhysicallyDown")
        android_branch = function.index("$ChromeConfig.TouchpadPinchToAndroid")
        begin = function.index("BeginScrcpyPinch")
        self.assertLess(ctrl_check, android_branch)
        self.assertLess(android_branch, begin)
        self.assertIn('$script:touchpadGestureKind = "device"', function)

    def test_ctrl_plus_native_touchpad_pinch_is_host_zoom_not_android_pinch(self):
        text = self.read("MirrorChrome.ps1")
        start = text.index("function Update-TouchpadGesture")
        end = text.index("$script:gestureHook", start)
        function = text[start:end]
        self.assertIn("$ChromeConfig.CtrlTouchpadPinchToHostZoom", function)
        self.assertIn('$script:touchpadGestureKind = "host"', function)
        self.assertIn("Get-ClampedHostZoom", function)

    def test_windows_without_precision_touchpad_api_falls_back_cleanly(self):
        text = self.read("MirrorChrome.ps1")
        self.assertIn("ResolveTouchpadApi", text)
        self.assertIn("return false;", text)
        self.assertIn("$script:precisionTouchpadAvailable = $false", text)
        supervisor = self.read("Start-PhoneMirror.ps1")
        self.assertIn('$args.Add("--mouse=sdk")', supervisor)

    def test_mirror_toolbar_is_started_for_every_scrcpy_session_and_cleaned_up(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("function Start-MirrorChrome", text)
        self.assertIn("MirrorChrome.ps1", text)
        self.assertIn("$chromeProcess = Start-MirrorChrome", text)
        self.assertIn("Stop-MirrorChrome $chromeProcess", text)

    def test_mirror_toolbar_sleep_button_uses_scrcpy_screen_off_shortcut(self):
        text = self.read("MirrorChrome.ps1")
        self.assertIn("Sleep phone", text)
        self.assertIn("$ChromeConfig.SleepButton", text)
        self.assertIn("SendScrcpyScreenOffShortcut", text)
        self.assertIn("VK_LMENU", text)
        self.assertIn("VK_O", text)
        self.assertIn("turns the Android physical display off while", text)

    def test_host_zoom_is_local_ctrl_wheel_and_has_reset_control(self):
        text = self.read("MirrorChrome.ps1")
        for contract in [
            "WH_MOUSE_LL",
            "WM_MOUSEWHEEL",
            "VK_CONTROL",
            "StartWheelHook",
            "TryDequeueWheel",
            "Magnification.dll",
            "MagSetWindowSource",
            "SetMagnifierTransform",
            "Reset zoom",
            "Reset-HostZoom",
        ]:
            self.assertIn(contract, text)

        self.assertIn("return new IntPtr(1)", text)
        self.assertIn("$script:zoom = 1.0", text)

    def test_host_zoom_is_view_only_and_does_not_inject_android_touch(self):
        text = self.read("MirrorChrome.ps1").lower()
        for forbidden in [
            "input swipe",
            "input tap",
            "input text",
            "adb.exe",
            "shell input",
        ]:
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, text)

    def test_stop_kills_mirror_toolbar_sidecars(self):
        text = self.read("Stop-PhoneMirror.ps1")
        self.assertIn("MirrorChrome\\.ps1", text)

    # ---------- Control center ----------

    def test_control_center_exposes_its_own_pc_side_preferences(self):
        xaml = self.read("ControlCenter.xaml")
        for control in [
            "PcControlCenterEnabledCheck",
            "PcControlCenterOpenCheck",
            "PcControlCenterDockCheck",
            "PcControlCenterTopmostCheck",
            "PcControlCenterRememberTabCheck",
            "PcAdvancedWritesEnabledCheck",
            "PcConfirmAdvancedWritesCheck",
            "PcControlCenterWidthText",
            "PcControlCenterHeightText",
        ]:
            self.assertIn(f'x:Name="{control}"', xaml)

        script = self.read("ControlCenter.ps1")
        for setting in [
            "ControlCenter.Enabled",
            "ControlCenter.OpenOnLaunch",
            "ControlCenter.DockToMirror",
            "ControlCenter.AlwaysOnTop",
            "ControlCenter.RememberLastTab",
            "ControlCenter.AdvancedSettingsWritesEnabled",
            "ControlCenter.ConfirmSensitiveDeviceWrites",
            "ControlCenter.Width",
            "ControlCenter.Height",
        ]:
            self.assertIn(setting, script)

    def test_control_center_has_organized_pc_device_advanced_and_diagnostics_tabs(self):
        xaml = self.read("ControlCenter.xaml")
        for header in [
            'Header="Controls"',
            'Header="Display"',
            'Header="Privileged"',
            'Header="PC / mirror settings"',
            'Header="Device settings"',
            'Header="Advanced Android"',
            'Header="Diagnostics"',
        ]:
            self.assertIn(header, xaml)

    def test_control_center_covers_scrcpy_runtime_surface(self):
        script = self.read("ScrcpyControl.ps1")
        for action in [
            "fullscreen", "fit", "pixel-perfect", "rotate-left", "rotate-right",
            "flip-horizontal", "flip-vertical", "pause", "resume",
            "reset-capture", "fps", "home", "back", "apps", "menu",
            "power", "sleep", "wake", "rotate-device", "notifications",
            "quick-settings", "collapse-panels", "volume-down", "volume-up",
            "copy", "cut", "paste-sync", "paste-inject", "keyboard-settings",
        ]:
            self.assertIn(action, script)

    def test_device_settings_are_capability_driven_and_runtime_enumerated(self):
        script = self.read("DeviceControl.ps1")
        for contract in [
            'settings","list"',
            'ValidateSet("system","secure","global")',
            "Get-AndroidSettingsNamespace",
            "Set-AndroidSetting",
            "Remove-AndroidSetting",
            "Get-AndroidCommandServices",
        ]:
            self.assertIn(contract, script)

    def test_sensitive_device_settings_have_guardrails(self):
        script = self.read("DeviceControl.ps1")
        for protected in [
            "adb_enabled",
            "development_settings_enabled",
            "android_id",
            "bluetooth_address",
        ]:
            self.assertIn(protected, script)
        self.assertIn('"protected"', script)

    def test_control_center_is_reachable_from_always_on_mirror_toolbar(self):
        chrome = self.read("MirrorChrome.ps1")
        self.assertIn('New-ToolbarButton "Controls"', chrome)
        self.assertIn("Open-ControlCenter", chrome)
        self.assertIn("ControlCenter.ps1", chrome)

    def test_control_center_and_runtime_artifacts_stop_with_package(self):
        stop = self.read("Stop-PhoneMirror.ps1")
        self.assertIn("ControlCenter\\.ps1", stop)
        chrome = self.read("MirrorChrome.ps1")
        self.assertIn("controlCenterProcess", chrome)

    def test_scrcpy_session_preferences_are_applied_to_launch_arguments(self):
        supervisor = self.read("Start-PhoneMirror.ps1")
        for option in [
            "--video-codec=",
            "--no-audio",
            "--audio-codec=",
            "--audio-dup",
            "--audio-buffer=",
            "--fullscreen",
            "--always-on-top",
            "--disable-screensaver",
            "--record=",
        ]:
            self.assertIn(option, supervisor)
        self.assertIn("Split-ExtraScrcpyArguments", supervisor)
        self.assertIn("cannot override required Android Headless Mirror option", supervisor)

    # ---------- REX CLI ----------

    def test_rex_cli_builds_on_dotnet_8_lts(self):
        sdk = json.loads(self.read("global.json"))["sdk"]
        self.assertEqual(sdk["version"], "8.0.100")
        self.assertEqual(sdk["rollForward"], "latestFeature")
        self.assertFalse(sdk["allowPrerelease"])

    def test_rex_cli_uses_stable_spectre_console_and_self_contained_publish(self):
        project = self.read("src/Rex.AndroidMirror.Cli/Rex.AndroidMirror.Cli.csproj")
        self.assertIn('Spectre.Console" Version="0.57.2"', project)
        self.assertIn("<PublishSingleFile Condition=", project)
        self.assertIn("<SelfContained Condition=", project)
        self.assertIn(">true</PublishSingleFile>", project)
        self.assertIn(">true</SelfContained>", project)
        self.assertIn("<TargetFramework>net8.0</TargetFramework>", project)

    def test_rex_cli_has_guided_and_scripted_entry_points(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        app = self.read("src/Rex.AndroidMirror.Cli/RexApp.cs")
        for command in [
            '"status"', '"devices"', '"start"', '"stop"', '"setup"', '"repair"',
            '"autostart"', '"shortcut"', '"controls"', '"action"', '"mirror"', '"display"', '"root"', '"device"',
            '"android"', '"config"', '"screenshot"', '"lock-mode"', '"reset-lock"',
            '"captures"', '"diagnostics"',
        ]:
            self.assertIn(command, program)
        for section_name in [
            "Runtime controls",
            "Display transports",
            "Privileged Android",
            "PC / mirror settings",
            "Device settings",
            "Advanced Android settings",
            "Open GUI Control Center",
            "Setup / repair",
            "Windows startup",
            "Desktop shortcut",
            "Captures",
        ]:
            self.assertIn(section_name, app)

    def test_display_transport_architecture_is_exposed_across_surfaces(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        machine = self.read("src/Rex.AndroidMirror.Cli/MachineMode.cs")
        app = self.read("src/Rex.AndroidMirror.Cli/RexApp.cs")
        xaml = self.read("ControlCenter.xaml")
        manager = self.read("src/Rex.AndroidMirror.Cli/DisplayManager.cs")
        docs = self.read("docs/DISPLAY_TRANSPORTS.md")

        for needle in [
            "display probe",
            "display receiver open",
            "display verify",
        ]:
            self.assertIn(needle, program)
        self.assertIn('case "display"', machine)
        self.assertIn("Display transports", app)
        self.assertIn('x:Name="DisplayTab"', xaml)
        self.assertIn("DisplayTransportIds.Scrcpy", manager)
        self.assertIn("DisplayTransportIds.WindowsMiracast", manager)
        self.assertIn("ADB remains the independent control plane", docs)
        self.assertNotIn("black frame detector", manager.lower())

    def test_root_privilege_architecture_is_capability_driven_and_read_only(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        machine = self.read("src/Rex.AndroidMirror.Cli/MachineMode.cs")
        app = self.read("src/Rex.AndroidMirror.Cli/RexApp.cs")
        manager = self.read("src/Rex.AndroidMirror.Cli/RootManager.cs")
        shell = self.read("src/Rex.AndroidMirror.Cli/AndroidShellRunner.cs")
        features = self.read("src/Rex.AndroidMirror.Cli/RootFeatureService.cs")
        xaml = self.read("ControlCenter.xaml")
        agents = self.read("AGENTS.md")

        self.assertIn('case "root"', program)
        self.assertIn('case "root"', machine)
        self.assertIn("Privileged Android", app)
        self.assertIn('x:Name="PrivilegedTab"', xaml)
        self.assertIn("ProbePassiveAsync", manager)
        self.assertIn("RequestAsync", manager)
        self.assertIn("RootAccessState.GrantedRestricted", manager)
        self.assertIn("RootExecutionMode.Su", shell)
        self.assertIn("AndroidShellQuoting", shell)
        self.assertIn("PrivilegeRisk.ReadOnly", features)
        self.assertIn("REX.bat agent root status", agents)

        # Root v1 is deliberately an inspection platform, not a raw mutation shell.
        self.assertNotIn("flash", features.lower())
        self.assertNotIn("dd if=", features.lower())
        self.assertNotIn("mount -o rw", features.lower())
        self.assertNotIn("root exec", machine.lower())
        self.assertNotIn("raw shell", program.lower())

    def test_privileged_root_architecture_is_exposed_safely_across_surfaces(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        machine = self.read("src/Rex.AndroidMirror.Cli/MachineMode.cs")
        app = self.read("src/Rex.AndroidMirror.Cli/RexApp.cs")
        xaml = self.read("ControlCenter.xaml")
        router = self.read("src/Rex.AndroidMirror.Cli/RootCommandRouter.cs")
        manager = self.read("src/Rex.AndroidMirror.Cli/RootManager.cs")
        executor = self.read("src/Rex.AndroidMirror.Cli/AndroidShellRunner.cs")
        policy = self.read("src/Rex.AndroidMirror.Cli/RootPolicy.cs")
        features = self.read("src/Rex.AndroidMirror.Cli/RootFeatureService.cs")

        for needle in [
            "rex root status",
            "rex root request",
            "rex root diagnostics",
            "rex root files",
        ]:
            self.assertIn(needle, program)

        self.assertIn('case "root"', machine)
        self.assertIn("Privileged Android", app)
        self.assertIn('x:Name="PrivilegedTab"', xaml)
        self.assertIn("passiveStatusDoesNotInvokeSu", machine)
        self.assertIn("rootV1Writes = false", machine)
        self.assertIn("rawShell = false", machine)
        self.assertIn("RootExecutionMode.Su", manager)
        self.assertIn("su", executor)
        self.assertIn("PrivilegeRisk.ReadOnly", features)
        self.assertIn("EnsureAllowed(PrivilegeRisk.ReadOnly)", features)
        self.assertIn("AllowDeviceCritical", policy)

        # Root v1 deliberately has no arbitrary privileged shell or flashing path.
        self.assertNotIn('case "exec"', router)
        self.assertNotIn('"root exec', program.lower())
        self.assertNotIn('"root shell', program.lower())
        self.assertNotIn("flash", router.lower())
        self.assertNotIn("boot image", router.lower())

    def test_rex_cli_first_run_wizard_covers_headless_setup_choices(self):
        app = self.read("src/Rex.AndroidMirror.Cli/RexApp.cs")
        for prompt in [
            "Run guided setup now?",
            "Start Android Headless Mirror automatically",
            "Keep the physical phone display off",
            "Keep Android awake while USB power is connected",
            "Precision Touchpad gestures",
            "Open the GUI Control Center automatically",
            "Start Android Headless Mirror now?",
        ]:
            self.assertIn(prompt, app)
        self.assertIn("ConfigureLockScreenAsync", app)
        self.assertIn("WaitForDevicesAsync", app)

    def test_rex_cli_exposes_every_config_and_live_android_namespaces(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        config_store = self.read("src/Rex.AndroidMirror.Cli/ConfigStore.cs")
        bridge = self.read("RexBridge.ps1")
        self.assertIn("config list", program)
        self.assertIn("config get", program)
        self.assertIn("config set", program)
        self.assertIn("config restore", program)
        self.assertIn("Flatten()", config_store)
        self.assertIn('"settings-list"', bridge)
        self.assertIn('"settings-get"', bridge)
        self.assertIn('"settings-set"', bridge)
        self.assertIn('"settings-delete"', bridge)

    def test_rex_cli_config_writes_are_atomic_and_recoverable(self):
        store = self.read("src/Rex.AndroidMirror.Cli/ConfigStore.cs")
        for needle in [
            'BackupPath => _path + ".rex-backup"',
            "SaveAtomic",
            "RestoreBackup",
            "File.Move(temp, _path, true)",
            "JsonNode.Parse",
        ]:
            self.assertIn(needle, store)

    def test_rex_cli_bridge_reuses_existing_control_backends(self):
        bridge = self.read("RexBridge.ps1")
        self.assertIn('DeviceControl.ps1', bridge)
        self.assertIn('ScrcpyControl.ps1', bridge)
        self.assertIn("Invoke-ScrcpyNamedShortcut", bridge)
        self.assertIn("Set-FriendlyAndroidSetting", bridge)
        self.assertIn("Get-AndroidSettingsNamespace", bridge)

    def test_rex_cli_host_zoom_has_gui_parity(self):
        bridge = self.read("RexBridge.ps1")
        chrome = self.read("MirrorChrome.ps1")
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        for command in ["zoom-in", "zoom-out", "reset-zoom"]:
            self.assertIn(command, bridge)
            self.assertIn(command, chrome)
            self.assertIn(command, program)

    def test_rex_cli_bootstrap_verifies_release_checksum_and_has_local_build_fallback(self):
        bootstrap = self.read("Bootstrap-RexCli.ps1")
        for needle in [
            "rex-win-x64.zip",
            "rex-win-x64.zip.sha256",
            "Get-FileHash",
            "SHA256",
            "dotnet",
            "PublishSingleFile=true",
        ]:
            self.assertIn(needle, bootstrap)
        self.assertLess(bootstrap.index("Get-FileHash"), bootstrap.index("Expand-Archive"))

    def test_rex_release_workflow_publishes_zip_and_checksum(self):
        workflow = self.read(".github/workflows/release.yml")
        self.assertIn("rex-win-x64.zip", workflow)
        self.assertIn("rex-win-x64.zip.sha256", workflow)
        self.assertIn("Get-FileHash", workflow)
        self.assertIn("gh release create", workflow)
        self.assertIn("contents: write", workflow)

    def test_rex_cli_has_real_bridge_fake_adb_e2e(self):
        bridge_test = self.read("tests/Test-RexBridgeE2E.ps1")
        for needle in [
            'Invoke-Bridge "status"',
            'Invoke-Bridge "friendly-set"',
            'Invoke-Bridge "settings-list"',
            'Invoke-Bridge "settings-set"',
            'Invoke-Bridge "set-lock-mode"',
            'Invoke-Bridge "mirror-command"',
            'Invoke-Bridge "cmd-services"',
            "fake-adb.cmd",
            "REX_ADB_PATH",
            "REX_SCRCPY_PATH",
        ]:
            self.assertIn(needle, bridge_test)

    def test_rex_cli_has_dedicated_unit_and_interactive_tests(self):
        required = [
            "tests/Rex.AndroidMirror.Cli.Tests/ConfigStoreTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/AppPathsTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/BridgeClientTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/ProgramTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RexBrandTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RexAppTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/MachineModeTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/SmartLaunchTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/DisplayManagerTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/WindowsDisplayHostProbeTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/DisplayVerificationStoreTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/AndroidShellRunnerTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RootManagerTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RootPolicyTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RootStateStoreTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RootFeatureServiceTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/RootCommandRouterTests.cs",
            "tests/Rex.AndroidMirror.Cli.Tests/DeviceCapabilityRegistryTests.cs",
        ]
        for path in required:
            with self.subTest(path=path):
                self.assertTrue((ROOT / path).is_file())

        wizard = self.read("tests/Rex.AndroidMirror.Cli.Tests/RexAppTests.cs")
        self.assertIn("FirstRunWizard_ConfiguresToolsStartupLockAndHeadlessDefaults", wizard)
        self.assertIn("RuntimeControls_DispatchSleepAndReturnToMainMenu", wizard)
        self.assertIn("AdvancedAndroid_ProtectedKeyNeverDispatchesAWrite", wizard)

    def test_rex_plain_machine_mode_and_agents_contract_are_present(self):
        program = self.read("src/Rex.AndroidMirror.Cli/Program.cs")
        machine = self.read("src/Rex.AndroidMirror.Cli/MachineMode.cs")
        agents = self.read("AGENTS.md")
        rex_bat = self.read("REX.bat")
        bootstrap = self.read("Bootstrap-RexCli.ps1")

        self.assertIn('"agent"', program)
        self.assertIn('"--plain"', program)
        self.assertIn('"--json"', program)
        self.assertIn("GetMachineArgs", program)
        self.assertIn("ProtocolVersion = 1", machine)
        self.assertIn("non-interactive-json", machine)
        self.assertIn("exactly one JSON document", agents)
        self.assertIn("REX.bat agent capabilities", agents)
        self.assertIn("REX.bat status --json", agents)
        self.assertIn("REX.bat agent display probe", agents)
        self.assertIn("REX.bat agent root status", agents)
        self.assertIn("REX.bat agent root capabilities", agents)
        self.assertIn("REX_BOOTSTRAP_QUIET", rex_bat)
        self.assertIn('if /I "%~1"=="agent"', rex_bat)
        self.assertIn("for %%A in (%*)", rex_bat)
        self.assertIn("-Quiet", rex_bat)
        self.assertIn("[switch]$Quiet", bootstrap)

        # Keep one machine contract. Do not let a second agent implementation drift.
        self.assertFalse((ROOT / "src/Rex.AndroidMirror.Cli/AgentCli.cs").exists())

    def test_rex_smart_desktop_shortcut_is_first_class_and_state_driven(self):
        install = self.read("Install-Rex-Shortcut.ps1")
        remove = self.read("Remove-Rex-Shortcut.ps1")
        smart = self.read("src/Rex.AndroidMirror.Cli/SmartLaunch.cs")
        setup = self.read("Setup.ps1")
        self.assertIn("REX.lnk", install)
        self.assertIn("REX.bat", install)
        self.assertIn(" smart", install)
        self.assertIn("REX.lnk", remove)
        self.assertIn("Install-Rex-Shortcut.ps1", setup)
        for state in [
            "SetupRequired",
            "FocusMirror",
            "StartMirror",
            "StartAndWaitForDevice",
            "WaitForDevice",
        ]:
            self.assertIn(state, smart)
        self.assertIn("PersistentOff", smart)
        self.assertIn("MirrorRunning", smart)
        self.assertIn('x.State == "device"', smart)

    def test_rex_smart_launch_has_exhaustive_state_and_shortcut_tests(self):
        smart_tests = self.read("tests/Rex.AndroidMirror.Cli.Tests/SmartLaunchTests.cs")
        shortcut_test = self.read("tests/Test-RexShortcut.ps1")
        for needle in [
            "Planner_SetupIncomplete_RequiresWizard",
            "Planner_PersistentOffWithAuthorizedDevice_IsExplicitStart",
            "Planner_ActiveMirror_FocusesInsteadOfStartingDuplicate",
            "Planner_UnauthorizedOnly_IsNotTreatedAsReady",
            "Launcher_StartMirror_ClearsPersistentOffAndUsesCanonicalLauncher",
            "Launcher_ActiveMirror_FocusesWindowAndDoesNotStartAgain",
            "Launcher_ExistingWaitingSupervisor_DoesNotStartDuplicate",
            "Launcher_StartFailure_IsSurfaced",
        ]:
            self.assertIn(needle, smart_tests)
        self.assertIn("REX.lnk", shortcut_test)
        self.assertIn("smart", shortcut_test)
        self.assertIn("WorkingDirectory", shortcut_test)
        self.assertIn("idempotent", shortcut_test)

    # ---------- Setup/install security ----------

    def test_setup_verifies_scrcpy_checksum_before_extracting(self):
        text = self.read("Setup.ps1")
        for needle in [
            "Genymobile/scrcpy/releases/latest",
            "SHA256SUMS.txt",
            "Get-FileHash",
            "Checksum verification failed",
            "Expand-Archive",
        ]:
            self.assertIn(needle, text)

        self.assertLess(text.index("Get-FileHash"), text.index("Expand-Archive"))
        self.assertLess(
            text.index("Checksum verification failed"),
            text.index("Expand-Archive"),
        )

    def test_setup_uses_tls_and_cleans_temp_directory(self):
        text = self.read("Setup.ps1")
        self.assertIn("Tls12", text)
        self.assertIn("finally", text)
        self.assertIn("Remove-Item -Recurse -Force $TempDir", text)

    def test_setup_does_not_request_elevation(self):
        text = self.read("Setup.ps1")
        forbidden = ["-Verb RunAs", "runas.exe", "Start-Process powershell -Verb"]
        for needle in forbidden:
            self.assertNotIn(needle, text)

    def test_autostart_is_per_user_and_branded(self):
        install = self.read("Install-Autostart.ps1")
        remove = self.read("Remove-Autostart.ps1")
        self.assertIn('GetFolderPath("Startup")', install)
        self.assertIn("Android Headless Mirror.lnk", install)
        self.assertIn("Android Headless Mirror.lnk", remove)
        self.assertIn("S21 Headless Mirror.lnk", remove)
        self.assertNotIn("schtasks", install.lower())

    # ---------- Lifecycle/supervisor ----------

    def test_supervisor_respects_persistent_stop_before_mutex(self):
        text = self.read("Start-PhoneMirror.ps1")
        stop_check = text.index("if (Test-Path $StopFile)")
        mutex = text.index("AndroidHeadlessMirrorSupervisor")
        self.assertLess(stop_check, mutex)
        self.assertNotIn("Remove-Item -Force $StopFile", text)

    def test_supervisor_uses_single_instance_mutex(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("System.Threading.Mutex", text)
        self.assertIn("AndroidHeadlessMirrorSupervisor", text)
        self.assertIn("if (-not $createdNew)", text)

    def test_clean_scrcpy_exit_rearms_after_disconnect(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("if ($exitCode -eq 0)", text)
        self.assertIn("Wait-ForDeviceDisconnect", text)
        clean_exit = text[text.index("if ($exitCode -eq 0)") :]
        self.assertIn("continue", clean_exit)
        self.assertIn("armed for automatic launch on next connection", text)

    def test_unexpected_restart_is_configurable(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("RestartOnUnexpectedExit", text)
        self.assertIn("RetrySeconds", text)

    def test_scrcpy_launch_preserves_argument_boundaries(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("function Invoke-Scrcpy", text)
        self.assertIn("& $Executable @Arguments", text)
        self.assertIn("Invoke-Scrcpy $Scrcpy $args", text)
        self.assertNotIn("Start-Process -FilePath $Scrcpy -ArgumentList $args", text)

    def test_scrcpy_native_stderr_does_not_use_global_stop_policy(self):
        text = self.read("Start-PhoneMirror.ps1")
        start = text.index("function Invoke-Scrcpy")
        end = text.index("function Prepare-DeviceForMirror", start)
        invoke = text[start:end]
        self.assertIn('$previousErrorActionPreference = $ErrorActionPreference', invoke)
        self.assertIn('$ErrorActionPreference = "Continue"', invoke)
        self.assertIn('$ErrorActionPreference = $previousErrorActionPreference', invoke)
        self.assertIn('PSNativeCommandUseErrorActionPreference', invoke)

    def test_scrcpy_window_title_is_unique_per_device_for_overlay_tracking(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn('$sessionTitle = "{0} [{1}]"', text)
        self.assertIn('$args.Add("--window-title=$sessionTitle")', text)

    def test_scrcpy_arguments_cover_expected_controls(self):
        text = self.read("Start-PhoneMirror.ps1")
        for arg in [
            "--serial=",
            "--window-title=",
            "--mouse=sdk",
            "--turn-screen-off",
            "--stay-awake",
            "--keep-active",
            "--power-off-on-close",
            "--max-size=",
            "--max-fps=",
            "--video-bit-rate=",
        ]:
            self.assertIn(arg, text)

    def test_device_preparation_wakes_and_best_effort_dismisses_keyguard(self):
        text = self.read("Start-PhoneMirror.ps1")
        start = text.index("function Prepare-DeviceForMirror")
        end = text.index("function Wait-ForDeviceDisconnect", start)
        prepare = text[start:end]
        self.assertIn("KEYCODE_WAKEUP", prepare)
        self.assertIn("wm dismiss-keyguard", prepare)
        self.assertIn("DismissKeyguardWhenPossible", prepare)
        self.assertIn("authentication is not", prepare)

    def test_any_authorized_usb_device_can_fallback_when_preferred_is_absent(self):
        text = self.read("Start-PhoneMirror.ps1")
        start = text.index("function Select-Device")
        end = text.index("function Build-ScrcpyArguments", start)
        select = text[start:end]
        self.assertIn("preferred", select)
        self.assertIn("if ($Config.PreferUsb)", select)
        self.assertIn("return $usb[0]", select)
        self.assertNotIn("LockToPreferredDevice", select)

    def test_unauthorized_secondary_phone_does_not_interrupt_usable_device(self):
        text = self.read("Start-PhoneMirror.ps1")
        loop_start = text.index("while (-not (Test-Path $StopFile))", text.index("$State = Load-State"))
        loop = text[loop_start:]
        selected = loop.index("$selected = Select-Device")
        hint = loop.index("Show-FirstUseHint")
        no_selected = loop.index("if (-not $selected)")
        self.assertLess(selected, no_selected)
        self.assertLess(no_selected, hint)

    def test_unexpected_disconnect_returns_to_fast_connection_poll(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("$afterExit = @(Get-AdbDevices $Adb)", text)
        self.assertIn("$stillOnline", text)
        self.assertIn("Start-Sleep -Seconds ([int]$Config.RetrySeconds)", text)
        self.assertEqual(json.loads(self.read("config.json"))["PollSeconds"], 1)

    def test_wireless_tcpip_is_guarded_by_two_config_flags(self):
        text = self.read("Start-PhoneMirror.ps1")
        function_start = text.index("function Configure-WirelessFromUsb")
        function_end = text.index("function Select-Device", function_start)
        function = text[function_start:function_end]
        self.assertIn("Wireless.Enabled", function)
        self.assertIn("EnableTcpipWhenUsbAvailable", function)
        self.assertIn("tcpip", function)
        self.assertLess(function.index("Wireless.Enabled"), function.index("tcpip"))
        self.assertLess(
            function.index("EnableTcpipWhenUsbAvailable"),
            function.index("tcpip"),
        )

    def test_stop_persists_off_state_and_never_kills_adb_server(self):
        stop = self.read("Stop-PhoneMirror.ps1")
        self.assertIn("Set-Content -Path $StopFile", stop)
        self.assertIn("Start-PhoneMirror", stop)
        self.assertIn("scrcpy.exe", stop)
        self.assertIn("[regex]::Escape($Root)", stop)
        self.assertNotIn("kill-server", stop)
        self.assertNotIn("adb.exe", stop)

    def test_all_explicit_start_entrypoints_clear_stop_flag(self):
        for path in [
            "START_NOW.bat",
            "START_WITH_LOG.bat",
            "SETUP_AND_START.bat",
        ]:
            text = self.read(path).lower()
            with self.subTest(path=path):
                self.assertIn("stop.flag", text)
                self.assertIn("del /q", text)

    def test_batch_launchers_use_relative_paths_and_execution_policy_bypass(self):
        for path in [
            "SETUP_AND_START.bat",
            "START_WITH_LOG.bat",
            "STOP.bat",
            "DIAGNOSTICS.bat",
            "REMOVE_AUTOSTART.bat",
        ]:
            text = self.read(path)
            with self.subTest(path=path):
                self.assertIn("%~dp0", text)
                self.assertIn("-ExecutionPolicy Bypass", text)

    # ---------- Diagnostics ----------

    def test_diagnostics_distinguishes_adb_states(self):
        text = self.read("Diagnostics.ps1")
        for state in [
            "device",
            "unauthorized",
            "offline",
            "NOT DETECTED",
            "AUTHORIZED",
            "UNAUTHORIZED",
        ]:
            self.assertIn(state, text)
        self.assertIn("ro.product.manufacturer", text)
        self.assertIn("ro.product.model", text)

    def test_diagnostics_does_not_mutate_device_transport(self):
        text = self.read("Diagnostics.ps1")
        adb_commands = re.findall(r"&\s+\$adb[^\r\n]*", text, flags=re.I)
        self.assertGreater(len(adb_commands), 0)

        for command in adb_commands:
            lowered = command.lower()
            for destructive in [
                "kill-server",
                " tcpip ",
                " reboot",
                " shell rm ",
                " uninstall",
            ]:
                with self.subTest(command=command, destructive=destructive):
                    self.assertNotIn(destructive, lowered)

    # ---------- README/branding ----------

    def test_readme_is_generic_android_with_honest_tested_hardware_claim(self):
        readme = self.read("README.md")
        self.assertIn("Android devices generally", readme)
        self.assertIn("Samsung Galaxy S21 Ultra", readme)
        self.assertIn("SM-G998B", readme)
        self.assertIn("A REX Technologies product.", readme)

    def test_rex_brand_tokens_match_flint_canonical_palette(self):
        css = self.read("docs/styles.css")
        for token, value in REX_COLORS.items():
            pattern = rf"{re.escape(token)}\s*:\s*{re.escape(value)}\s*;"
            with self.subTest(token=token):
                self.assertRegex(css, pattern)

        for token, value in {
            "--radius-s": "8px",
            "--radius-m": "12px",
            "--radius-l": "18px",
        }.items():
            self.assertRegex(
                css,
                rf"{re.escape(token)}\s*:\s*{re.escape(value)}\s*;",
            )

    def test_old_standalone_site_palette_is_gone(self):
        combined = (
            self.read("docs/styles.css")
            + self.read("docs/index.html")
            + self.read("docs/404.html")
            + self.read("docs/favicon.svg")
        ).lower()
        for old in [
            "#090b10",
            "#82f7b0",
            "#78a7ff",
            "#f5f7fb",
            "#969eae",
        ]:
            self.assertNotIn(old, combined)

    def test_pages_brand_lockup_names_rex_technologies(self):
        html = self.read("docs/index.html")
        self.assertIn("REX Technologies", html)
        self.assertIn("brand-company", html)
        self.assertIn("Android Headless Mirror, by REX Technologies", html)
        self.assertIn("A REX Technologies product", html)

    def test_quick_start_opens_github_in_new_tab(self):
        html = self.read("docs/index.html")
        match = re.search(
            r'<a class="text-link"[^>]*href="https://github\.com/tochi-mba/Android_Headless_Mirror#quick-start"[^>]*>',
            html,
        )
        self.assertIsNotNone(match)
        self.assertIn('target="_blank"', match.group(0))
        self.assertIn('rel="noopener noreferrer"', match.group(0))

    # ---------- Static Pages contracts ----------

    def test_pages_required_files_exist(self):
        for path in [
            "docs/index.html",
            "docs/styles.css",
            "docs/app.js",
            "docs/favicon.svg",
            "docs/site.webmanifest",
            "docs/404.html",
            "docs/robots.txt",
            "docs/sitemap.xml",
            "docs/.nojekyll",
        ]:
            with self.subTest(path=path):
                self.assertTrue((ROOT / path).is_file(), path)

    def test_page_source_contains_no_literal_escaped_newline_junk(self):
        self.assertNotIn("\\n", self.read("docs/index.html"))

    def test_page_has_unique_ids(self):
        page = self.scan_page()
        duplicates = sorted(
            key for key, count in Counter(page.ids).items() if count > 1
        )
        self.assertEqual(duplicates, [])

    def test_all_internal_anchor_targets_exist(self):
        page = self.scan_page()
        ids = set(page.ids)
        anchors = [link[1:] for link in page.links if link.startswith("#")]
        self.assertEqual(sorted(set(anchors) - ids), [])

    def test_all_local_page_assets_exist(self):
        page = self.scan_page()
        missing = []
        for asset in page.assets:
            parsed = urlparse(asset)
            if parsed.scheme or asset.startswith("//"):
                continue
            if asset.startswith("#"):
                continue
            asset_path = asset.split("?", 1)[0].split("#", 1)[0]
            if not asset_path:
                continue
            if not (ROOT / "docs" / asset_path).exists():
                missing.append(asset_path)
        self.assertEqual(missing, [])

    def test_pages_document_privileged_root_safety_boundary(self):
        html = self.read("docs/index.html")
        self.assertIn('id="privileged"', html)
        self.assertEqual(html.count('id="privileged"'), 1)
        self.assertIn('href="#privileged"', html)
        self.assertIn("Root is a capability. Not a boolean.", html)
        self.assertIn("rex root status", html)
        self.assertIn("rex root request", html)
        self.assertIn("no root installation", html)
        self.assertIn("DRM bypass", html)

    def test_page_has_semantic_landmarks(self):
        page = self.scan_page()
        for tag in ["header", "nav", "main", "footer"]:
            self.assertGreaterEqual(page.tags[tag], 1, tag)

    def test_page_has_skip_link_and_main_target(self):
        html = self.read("docs/index.html")
        self.assertIn('class="skip-link" href="#main"', html)
        self.assertIn('<main id="main">', html)

    def test_all_buttons_have_explicit_type(self):
        page = self.scan_page()
        missing = [attrs for attrs in page.buttons if not attrs.get("type")]
        self.assertEqual(missing, [])

    def test_no_inline_event_handlers(self):
        page = self.scan_page()
        self.assertEqual(page.inline_handlers, [])

    def test_external_links_are_https(self):
        page = self.scan_page()
        invalid = []
        for href in page.links:
            parsed = urlparse(href)
            if parsed.scheme and parsed.scheme != "https":
                invalid.append(href)
        self.assertEqual(invalid, [])

    def test_page_metadata_is_complete_and_rex_branded(self):
        page = self.scan_page()
        self.assertIn("REX Technologies", page.title)
        self.assertEqual(page.meta.get("theme-color"), "#080A09")
        self.assertEqual(page.meta.get("color-scheme"), "dark")
        self.assertIn("REX Technologies", page.meta.get("og:title", ""))
        self.assertTrue(page.meta.get("description"))
        self.assertTrue(page.meta.get("og:description"))

    def test_manifest_is_valid_and_uses_rex_theme(self):
        manifest = json.loads(self.read("docs/site.webmanifest"))
        self.assertEqual(manifest["theme_color"], "#080A09")
        self.assertEqual(manifest["background_color"], "#080A09")
        self.assertEqual(manifest["display"], "standalone")
        self.assertTrue(manifest["icons"])

    def test_robots_and_sitemap_point_to_canonical_site(self):
        expected = "https://tochi-mba.github.io/Android_Headless_Mirror/"
        self.assertIn(expected, self.read("docs/robots.txt"))
        self.assertIn(expected, self.read("docs/sitemap.xml"))
        self.assertIn(expected, self.read("docs/index.html"))

    def test_pages_has_accessibility_and_motion_fallbacks(self):
        html = self.read("docs/index.html")
        css = self.read("docs/styles.css")
        self.assertIn("prefers-reduced-motion", css)
        self.assertIn(":focus-visible", css)
        self.assertIn('class="no-js"', html)
        self.assertIn("classList.replace", html)
        self.assertIn(".no-js .site-nav", css)

    def test_pages_javascript_has_expected_interactions(self):
        js = self.read("docs/app.js")
        for contract in [
            "aria-expanded",
            "aria-selected",
            "navigator.clipboard",
            "IntersectionObserver",
            "prefers-reduced-motion",
            "authorized",
            "unauthorized",
            "offline",
            "ArrowRight",
            "ArrowLeft",
            "Escape",
        ]:
            self.assertIn(contract, js)

    # ---------- Workflow contracts ----------

    def test_ci_smoke_enforces_root_v1_machine_safety_contract(self):
        workflow = self.read(".github/workflows/ci.yml")
        for needle in [
            "rootCommands",
            "root status",
            "root request",
            "passiveStatusDoesNotInvokeSu",
            "rawShell",
            "rootV1Writes",
        ]:
            self.assertIn(needle, workflow)

    def test_ci_has_package_and_browser_test_layers(self):
        workflow = self.read(".github/workflows/ci.yml")
        for required in [
            "windows-latest",
            "Test-PowerShellBehavior.ps1",
            "test_package.py",
            "ubuntu-latest",
            "playwright",
            "chromium",
            "npm run test:pages",
        ]:
            self.assertIn(required, workflow)

    def test_browser_test_dependencies_are_exactly_pinned(self):
        package = json.loads(self.read("package.json"))
        dev = package["devDependencies"]
        self.assertRegex(dev["@playwright/test"], r"^\d+\.\d+\.\d+$")
        self.assertRegex(dev["@axe-core/playwright"], r"^\d+\.\d+\.\d+$")
        self.assertFalse(dev["@playwright/test"].startswith("^"))
        self.assertFalse(dev["@axe-core/playwright"].startswith("^"))

    def test_pages_workflow_uses_official_pages_actions(self):
        workflow = self.read(".github/workflows/pages.yml")
        for action in [
            "actions/configure-pages@v6",
            "actions/upload-pages-artifact@v5",
            "actions/deploy-pages@v5",
        ]:
            self.assertIn(action, workflow)
        self.assertIn("path: docs", workflow)
        self.assertIn("pages: write", workflow)
        self.assertIn("id-token: write", workflow)

    # ---------- Pure state-machine edge cases ----------

    def test_adb_parser_handles_usb_tcp_and_non_ready_states(self):
        devices = parse_adb_devices(
            """List of devices attached
USB123 device product:x model:Pixel_9 transport_id:1
192.168.1.40:5555 device product:x model:Pixel_9 transport_id:2
AUTH unauthorized transport_id:3
OFF offline transport_id:4
NOPERM no permissions transport_id:5
garbage line
"""
        )
        self.assertEqual(len(devices), 5)
        self.assertFalse(devices[0]["is_tcp"])
        self.assertTrue(devices[1]["is_tcp"])
        self.assertEqual(
            [device["state"] for device in devices[2:]],
            ["unauthorized", "offline", "no permissions"],
        )

    def test_device_selection_prefers_configured_serial_then_usb(self):
        devices = parse_adb_devices(
            """List of devices attached
192.168.1.40:5555 device
USB1 device
USB2 device
"""
        )
        self.assertEqual(
            select_device(devices, preferred="USB2")["serial"],
            "USB2",
        )
        self.assertEqual(select_device(devices)["serial"], "USB1")

    def test_device_selection_can_prefer_first_ready_when_usb_preference_disabled(self):
        devices = parse_adb_devices(
            """List of devices attached
192.168.1.40:5555 device
USB1 device
"""
        )
        self.assertEqual(
            select_device(devices, prefer_usb=False)["serial"],
            "192.168.1.40:5555",
        )

    def test_device_selection_rejects_only_non_ready_devices(self):
        devices = parse_adb_devices(
            """List of devices attached
A unauthorized
B offline
C no permissions
"""
        )
        self.assertIsNone(select_device(devices))

    def test_private_ipv4_boundaries(self):
        good = [
            "10.0.0.0",
            "10.255.255.255",
            "172.16.0.0",
            "172.31.255.255",
            "192.168.0.0",
            "192.168.255.255",
        ]
        bad = [
            "0.0.0.0",
            "8.8.8.8",
            "127.0.0.1",
            "172.15.255.255",
            "172.32.0.0",
            "192.167.255.255",
            "192.169.0.0",
            "256.1.1.1",
            "1.2.3",
            "foo",
            "",
        ]
        for ip in good:
            with self.subTest(ip=ip):
                self.assertTrue(is_private_ipv4(ip))
        for ip in bad:
            with self.subTest(ip=ip):
                self.assertFalse(is_private_ipv4(ip))

    def test_phone_ip_candidates_prefer_wifi_interfaces_and_deduplicate(self):
        text = """3: rmnet0 inet 10.123.45.67/32 scope global rmnet0
12: swlan0 inet 192.168.43.1/24 scope global swlan0
13: wlan0 inet 192.168.43.1/24 scope global wlan0
14: rndis0 inet 192.168.42.129/24 scope global rndis0
"""
        self.assertEqual(phone_ip_candidates(text), ["192.168.43.1"])

    def test_phone_ip_candidates_fall_back_to_private_interfaces(self):
        text = """8: mystery0 inet 192.168.99.1/24 scope global mystery0
9: other0 inet 10.0.0.2/24 scope global other0
"""
        self.assertEqual(
            phone_ip_candidates(text),
            ["192.168.99.1", "10.0.0.2"],
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
