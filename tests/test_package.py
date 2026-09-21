import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def parse_adb_devices(text):
    out = []
    for line in text.splitlines():
        m = re.match(r"^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)", line)
        if m:
            serial, state = m.groups()
            out.append({"serial": serial, "state": state, "is_tcp": bool(re.search(r":\d+$", serial))})
    return out

def select_device(devices, preferred="", prefer_usb=True):
    ready = [d for d in devices if d["state"] == "device"]
    if not ready:
        return None
    if preferred:
        for d in ready:
            if d["serial"] == preferred:
                return d
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
    a,b,_,_ = nums
    return a == 10 or (a == 192 and b == 168) or (a == 172 and 16 <= b <= 31)

def phone_ip_candidates(text):
    preferred = []
    fallback = []
    for line in text.splitlines():
        m = re.match(r"^\d+:\s+([^:\s]+).*?\sinet\s+(\d+\.\d+\.\d+\.\d+)/", line)
        if not m:
            continue
        iface, ip = m.groups()
        if not is_private_ipv4(ip) or ip == "127.0.0.1":
            continue
        if ip not in fallback:
            fallback.append(ip)
        if re.search(r"(wlan|swlan|ap|softap|wifi)", iface, re.I) and ip not in preferred:
            preferred.append(ip)
    return preferred or fallback

def unique(values):
    out = []
    for v in values:
        s = str(v).strip()
        if s and s not in out:
            out.append(s)
    return out

class PackageTests(unittest.TestCase):
    def test_required_files_exist(self):
        for name in [
            "SETUP_AND_START.bat", "START_NOW.bat", "START_WITH_LOG.bat",
            "STOP.bat", "DIAGNOSTICS.bat", "REMOVE_AUTOSTART.bat",
            "Setup.ps1", "Install-Autostart.ps1", "Remove-Autostart.ps1",
            "Start-PhoneMirror.ps1", "Stop-PhoneMirror.ps1",
            "Start-Hidden.vbs", "config.json", "README.txt"
        ]:
            self.assertTrue((ROOT / name).exists(), name)

    def test_config_valid(self):
        cfg = json.loads((ROOT / "config.json").read_text())
        self.assertTrue(cfg["PreferUsb"])
        self.assertTrue(cfg["Wireless"]["Enabled"])
        self.assertEqual(cfg["Wireless"]["Port"], 5555)
        self.assertGreaterEqual(cfg["PollSeconds"], 1)
        self.assertGreater(cfg["Logging"]["MaxBytes"], 100_000)

    def test_setup_verifies_checksum_before_extract(self):
        s = (ROOT / "Setup.ps1").read_text()
        self.assertIn("Get-FileHash", s)
        self.assertIn("SHA256SUMS.txt", s)
        self.assertIn("Checksum verification failed", s)
        self.assertLess(s.index("Get-FileHash"), s.index("Expand-Archive"))

    def test_supervisor_reliability_features_present(self):
        s = (ROOT / "Start-PhoneMirror.ps1").read_text()
        for needle in [
            "S21HeadlessMirrorSupervisor",
            "unauthorized",
            "KEYCODE_WAKEUP",
            "--turn-screen-off",
            "--stay-awake",
            "adb tcpip",
            "Get-DefaultGatewayCandidates",
            "RestartOnUnexpectedExit",
            "Rotate-Log",
            "stop.flag",
        ]:
            self.assertIn(needle, s)

    def test_adb_parse_usb_and_tcp(self):
        text = """List of devices attached
R58M123ABC device product:p3sxxx model:SM_G998B transport_id:1
192.168.43.1:5555 device product:p3sxxx model:SM_G998B transport_id:2
X unauthorized usb:1-2 transport_id:3
"""
        d = parse_adb_devices(text)
        self.assertEqual(len(d), 3)
        self.assertFalse(d[0]["is_tcp"])
        self.assertTrue(d[1]["is_tcp"])
        self.assertEqual(d[2]["state"], "unauthorized")

    def test_usb_preferred_over_tcp(self):
        d = parse_adb_devices("""List of devices attached
192.168.43.1:5555 device
R58M123ABC device
""")
        self.assertEqual(select_device(d)["serial"], "R58M123ABC")

    def test_preferred_serial_wins(self):
        d = parse_adb_devices("""List of devices attached
USB1 device
USB2 device
""")
        self.assertEqual(select_device(d, preferred="USB2")["serial"], "USB2")

    def test_unauthorized_not_selected(self):
        d = parse_adb_devices("""List of devices attached
USB1 unauthorized
""")
        self.assertIsNone(select_device(d))

    def test_private_ipv4(self):
        good = ["10.0.0.1", "192.168.43.1", "172.16.0.1", "172.31.255.254"]
        bad = ["8.8.8.8", "172.32.0.1", "127.0.0.1", "999.1.2.3", "foo"]
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

    def test_unique_hosts(self):
        self.assertEqual(unique([" 192.168.43.1 ", "192.168.43.1", "", "10.0.0.2"]),
                         ["192.168.43.1", "10.0.0.2"])

    def test_vbs_launches_hidden_powershell(self):
        vbs = (ROOT / "Start-Hidden.vbs").read_text()
        self.assertIn("WindowStyle Hidden", vbs)
        self.assertIn("Start-PhoneMirror.ps1", vbs)
        self.assertRegex(vbs, r"shell\.Run cmd, 0, False")

    def test_stop_writes_signal_and_kills_scrcpy(self):
        s = (ROOT / "Stop-PhoneMirror.ps1").read_text()
        self.assertIn("stop.flag", s)
        self.assertIn('Get-Process -Name "scrcpy"', s)

if __name__ == "__main__":
    unittest.main(verbosity=2)
