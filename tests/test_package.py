import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def parse_adb_devices(text):
    out = []
    for line in text.splitlines():
        m = re.match(
            r"^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)",
            line,
        )
        if m:
            serial, state = m.groups()
            out.append(
                {
                    "serial": serial,
                    "state": state,
                    "is_tcp": bool(re.search(r":\d+$", serial)),
                }
            )
    return out


def select_device(devices, preferred="", prefer_usb=True):
    ready = [d for d in devices if d["state"] == "device"]
    if not ready:
        return None
    if preferred:
        for device in ready:
            if device["serial"] == preferred:
                return device
    if prefer_usb:
        usb = [d for d in ready if not d["is_tcp"]]
        if usb:
            return usb[0]
    return ready[0]


def is_private_ipv4(ip):
    try:
        nums = [int(x) for x in ip.split(".")]
    except ValueError:
        return False
    if len(nums) != 4 or any(n < 0 or n > 255 for n in nums):
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
        m = re.match(
            r"^\d+:\s+([^:\s]+).*?\sinet\s+(\d+\.\d+\.\d+\.\d+)/",
            line,
        )
        if not m:
            continue
        iface, ip = m.groups()
        if not is_private_ipv4(ip) or ip == "127.0.0.1":
            continue
        if ip not in fallback:
            fallback.append(ip)
        if re.search(r"(wlan|swlan|ap|softap|wifi)", iface, re.I):
            if ip not in preferred:
                preferred.append(ip)
    return preferred or fallback


class PackageTests(unittest.TestCase):
    def read(self, path):
        return (ROOT / path).read_text(encoding="utf-8-sig")

    def test_required_files_exist(self):
        for name in [
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
        ]:
            self.assertTrue((ROOT / name).exists(), name)

    def test_legacy_hard_stop_files_removed(self):
        for name in [
            "HARD_STOP.ps1",
            "HARD_STOP.bat",
            "HARD_STOP_AND_DISABLE_AUTOSTART.bat",
        ]:
            self.assertFalse((ROOT / name).exists(), name)

    def test_config_valid_and_safe_defaults(self):
        cfg = json.loads(self.read("config.json"))
        self.assertTrue(cfg["PreferUsb"])
        self.assertFalse(cfg["Wireless"]["Enabled"])
        self.assertFalse(cfg["Wireless"]["EnableTcpipWhenUsbAvailable"])
        self.assertEqual(cfg["Wireless"]["Port"], 5555)
        self.assertGreaterEqual(cfg["PollSeconds"], 1)
        self.assertGreater(cfg["Logging"]["MaxBytes"], 100_000)
        self.assertEqual(cfg["WindowTitle"], "Android Device")

    def test_setup_verifies_checksum_before_extract(self):
        text = self.read("Setup.ps1")
        self.assertIn("Get-FileHash", text)
        self.assertIn("SHA256SUMS.txt", text)
        self.assertIn("Checksum verification failed", text)
        self.assertLess(text.index("Get-FileHash"), text.index("Expand-Archive"))

    def test_supervisor_respects_persistent_stop_state(self):
        text = self.read("Start-PhoneMirror.ps1")
        self.assertIn("AndroidHeadlessMirrorSupervisor", text)
        self.assertIn("if (Test-Path $StopFile)", text)
        self.assertIn("exit 0", text)

        stop_check = text.index("if (Test-Path $StopFile)")
        mutex = text.index("AndroidHeadlessMirrorSupervisor")
        self.assertLess(stop_check, mutex)

        # The supervisor must never clear the persistent OFF state itself.
        self.assertNotIn("Remove-Item -Force $StopFile", text)

    def test_explicit_start_entrypoints_clear_stop_flag(self):
        for path in [
            "START_NOW.bat",
            "START_WITH_LOG.bat",
            "SETUP_AND_START.bat",
        ]:
            text = self.read(path).lower()
            self.assertIn("stop.flag", text, path)
            self.assertIn("del /q", text, path)

    def test_stop_is_persistent_and_does_not_kill_adb(self):
        text = self.read("Stop-PhoneMirror.ps1")
        self.assertIn("Set-Content -Path $StopFile", text)
        self.assertIn("Start-PhoneMirror", text)
        self.assertIn("scrcpy.exe", text)
        self.assertNotIn("kill-server", text)
        self.assertNotIn("adb.exe", text)

        launcher = self.read("STOP.bat")
        self.assertIn("Stop-PhoneMirror.ps1", launcher)
        self.assertNotIn("HARD_STOP", launcher)

    def test_generic_android_branding(self):
        supervisor = self.read("Start-PhoneMirror.ps1")
        diagnostics = self.read("Diagnostics.ps1")
        readme = self.read("README.md")

        self.assertIn("Android Headless Mirror", supervisor)
        self.assertIn("Android Headless Mirror Diagnostics", diagnostics)
        self.assertIn("Android devices generally", readme)
        self.assertIn("Samsung Galaxy S21 Ultra", readme)
        self.assertIn("SM-G998B", readme)

        self.assertNotIn("S21HeadlessMirrorSupervisor", supervisor)

    def test_diagnostics_distinguishes_adb_states(self):
        text = self.read("Diagnostics.ps1")
        for state in ["device", "unauthorized", "offline"]:
            self.assertIn(state, text)
        self.assertIn("NOT DETECTED", text)
        self.assertIn("AUTHORIZED", text)
        self.assertIn("UNAUTHORIZED", text)
        self.assertIn("ro.product.manufacturer", text)
        self.assertIn("ro.product.model", text)

    def test_adb_parse_usb_and_tcp(self):
        text = """List of devices attached
R58M123ABC device product:p3sxxx model:SM_G998B transport_id:1
192.168.43.1:5555 device product:p3sxxx model:SM_G998B transport_id:2
X unauthorized usb:1-2 transport_id:3
"""
        devices = parse_adb_devices(text)
        self.assertEqual(len(devices), 3)
        self.assertFalse(devices[0]["is_tcp"])
        self.assertTrue(devices[1]["is_tcp"])
        self.assertEqual(devices[2]["state"], "unauthorized")

    def test_usb_preferred_over_tcp(self):
        devices = parse_adb_devices(
            """List of devices attached
192.168.43.1:5555 device
R58M123ABC device
"""
        )
        self.assertEqual(select_device(devices)["serial"], "R58M123ABC")

    def test_preferred_serial_wins(self):
        devices = parse_adb_devices(
            """List of devices attached
USB1 device
USB2 device
"""
        )
        self.assertEqual(
            select_device(devices, preferred="USB2")["serial"],
            "USB2",
        )

    def test_unauthorized_not_selected(self):
        devices = parse_adb_devices(
            """List of devices attached
USB1 unauthorized
"""
        )
        self.assertIsNone(select_device(devices))

    def test_private_ipv4(self):
        good = [
            "10.0.0.1",
            "192.168.43.1",
            "172.16.0.1",
            "172.31.255.254",
        ]
        bad = [
            "8.8.8.8",
            "172.32.0.1",
            "127.0.0.1",
            "999.1.2.3",
            "foo",
        ]
        for ip in good:
            self.assertTrue(is_private_ipv4(ip), ip)
        for ip in bad:
            self.assertFalse(is_private_ipv4(ip), ip)

    def test_ip_extraction_prefers_hotspot_wifi_interfaces(self):
        iptext = """3: rmnet0    inet 10.123.45.67/32 scope global rmnet0
12: swlan0    inet 192.168.43.1/24 brd 192.168.43.255 scope global swlan0
13: rndis0    inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0
"""
        self.assertEqual(phone_ip_candidates(iptext), ["192.168.43.1"])

    def test_ip_extraction_falls_back_to_private(self):
        iptext = """8: mystery0    inet 192.168.99.1/24 scope global mystery0
"""
        self.assertEqual(phone_ip_candidates(iptext), ["192.168.99.1"])

    def test_vbs_launches_hidden_powershell(self):
        text = self.read("Start-Hidden.vbs")
        self.assertIn("WindowStyle Hidden", text)
        self.assertIn("Start-PhoneMirror.ps1", text)
        self.assertRegex(text, r"shell\.Run cmd, 0, False")


if __name__ == "__main__":
    unittest.main(verbosity=2)
