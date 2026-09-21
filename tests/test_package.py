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
            "Setup.ps1",
            "Install-Autostart.ps1",
            "Remove-Autostart.ps1",
            "Start-PhoneMirror.ps1",
            "Stop-PhoneMirror.ps1",
            "Start-Hidden.vbs",
            "config.json",
            "README.md",
            ".gitignore",
            ".github/workflows/ci.yml",
            ".github/workflows/pages.yml",
            "tests/Test-PowerShellBehavior.ps1",
            "tests/pages.spec.js",
            "playwright.config.js",
            "package.json",
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
        for entry in ["tools/", "logs/", "state.json", "stop.flag", "*.log"]:
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

    def test_scrcpy_arguments_cover_expected_controls(self):
        text = self.read("Start-PhoneMirror.ps1")
        for arg in [
            "--serial=",
            "--window-title=",
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
