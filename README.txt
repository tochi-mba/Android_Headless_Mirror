S21 HEADLESS MIRROR
===================

Goal
----
Turn your Galaxy S21 Ultra into a phone you can normally operate from your
Windows laptop without touching the phone screen.

The package uses scrcpy + ADB. USB is the reliable "recovery path"; Wi-Fi /
phone-hotspot TCP/IP is a best-effort fallback after it has been bootstrapped
over USB.

QUICK START
-----------
1. On the S21 Ultra, do this ONCE:

   Settings -> About phone -> Software information
   Tap "Build number" 7 times.

   Then:
   Settings -> Developer options -> USB debugging -> ON

2. Connect the phone to the laptop by USB.

3. Double-click:
      SETUP_AND_START.bat

   The setup downloads the latest official 64-bit Windows scrcpy release
   directly from Genymobile's GitHub release, downloads SHA256SUMS.txt, verifies
   the ZIP's SHA-256 hash, and only then extracts it.

4. The FIRST time Android asks:
      "Allow USB debugging?"
   tick:
      "Always allow from this computer"
   then tap Allow.

After that, the supervisor starts automatically at Windows login. It waits in
the background if the phone is off/disconnected, and launches scrcpy as soon as
an authorised device becomes available.

WHAT IT DOES
------------
- Starts at Windows sign-in without needing admin rights.
- Waits indefinitely for the S21 instead of failing if the phone is not ready.
- Prefers USB.
- Wakes the phone before mirroring.
- Mirrors the lock screen, so you can type your PIN from the laptop when ADB is
  available.
- Turns the physical phone display off while keeping the mirrored display live.
- Keeps the phone awake while connected by USB.
- Automatically remembers the USB serial number.
- Attempts to enable ADB TCP/IP on port 5555 whenever USB is available.
- Saves likely phone Wi-Fi/hotspot IP addresses.
- Can try the Windows default gateway as a hotspot fallback.
- Reconnects/retries after unexpected disconnects.
- Uses a single-instance mutex so startup cannot accidentally launch duplicates.
- Rotates logs.
- Includes a STOP script, diagnostics, and an autostart remover.

IMPORTANT SAMSUNG SETTINGS
--------------------------
For the closest thing to "I boot the phone and never touch its screen":

1. Keep USB debugging enabled.
2. On first USB-debugging prompt, choose "Always allow from this computer".
3. Check Samsung security settings for features that block USB while locked.
   Depending on your One UI version, this may be under:
     Settings -> Security and privacy -> More security settings
   If there is "Block USB connections while locked", it can prevent the
   headless workflow while the phone is locked.
4. Samsung "Auto Blocker" can also interfere with USB commands/debugging.
   Only change security settings if you understand and accept the physical
   security trade-off.

LOCKED VS REBOOTED
------------------
Normal lock/sleep:
  Once ADB is authorised and available, scrcpy can show the lock screen and you
  can enter your PIN from the laptop.

After a FULL PHONE REBOOT:
  Android/Samsung security can require the first unlock before some USB/data
  functionality becomes available. Whether ADB is usable before that first
  unlock depends on the phone's current security/One UI settings.

There is no script that can safely bypass Android's secure first-unlock
requirement. USB is therefore the most reliable recovery path.

WIRELESS / PHONE HOTSPOT
------------------------
Bluetooth alone cannot carry a scrcpy screen/control session.

This package uses ADB-over-TCP/IP as the optional wireless fallback.

When USB is available it runs:
  adb tcpip 5555

It then saves likely private IPv4 addresses from the phone. If your laptop is
connected to the S21's mobile hotspot, it can additionally try the Windows
default gateway because the gateway is commonly the hotspot-hosting phone.

IMPORTANT:
  Legacy ADB TCP/IP normally resets after the PHONE reboots. That means a purely
  wireless "cold boot from nothing" is not dependable. If the USB cable remains
  connected, the script can automatically bootstrap wireless again after the
  phone comes back.

USING IT
--------
SETUP_AND_START.bat
  First-time install + checksum verification + autostart + start.

START_NOW.bat
  Start the background supervisor manually.

START_WITH_LOG.bat
  Start the supervisor visibly so you can watch logs/errors.

STOP.bat
  Stop the mirror/supervisor.

DIAGNOSTICS.bat
  Show scrcpy/ADB versions, connected devices, saved state and recent logs.

REMOVE_AUTOSTART.bat
  Stop launching this package automatically when Windows signs in.

CONFIG
------
Edit config.json.

Useful values:
  "TurnPhysicalScreenOff": true
      Physical phone display off while mirrored.

  "StayAwakeWhenUsb": true
      Prevent sleep while plugged in.

  "PowerOffOnClose": false
      If true, scrcpy asks Android to power the display off when scrcpy closes.

  "MaxSize": 1920
      Maximum video dimension. Set 0 to avoid passing --max-size.

  "MaxFps": 60
      Set 0 to avoid passing --max-fps.

  "VideoBitRate": "12M"
      Quality/latency trade-off.

  "PreferredSerial": ""
      Leave blank; it auto-remembers the first authorised USB device.
      Or enter the S21 ADB serial manually.

  Wireless.ManualHosts
      Example:
        ["192.168.43.1"]
      Add a known phone-hotspot IP here if automatic discovery is not enough.

LOGS
----
logs\mirror.log

If something fails:
  1. Double-click DIAGNOSTICS.bat
  2. Double-click START_WITH_LOG.bat
  3. Check whether adb lists the phone as:
       device       -> good
       unauthorized -> unlock the phone once and approve USB debugging
       offline      -> reconnect USB / restart ADB / reboot if necessary

SECURITY NOTE
-------------
USB debugging gives an authorised computer powerful control over the phone.
Only permanently authorise a computer you trust. Turning off Samsung's
USB-while-locked protections weakens protection against someone with physical
access to your phone and laptop/cable.

TESTS INCLUDED
--------------
tests\test_package.py

The delivered package was tested for:
- valid JSON configuration;
- expected files and launch entry points;
- required safety/reliability features in the supervisor;
- ADB device-list parsing scenarios;
- USB preference / preferred-serial selection;
- private IPv4 classification;
- Wi-Fi/hotspot IP extraction behavior;
- duplicate host removal;
- startup and stop wiring;
- checksum-verification wiring.

The build environment used to create this ZIP is Linux and does not contain
Windows PowerShell, so the PowerShell files could not be hardware-executed
against a real S21 in this environment. The included tests therefore cover
package structure and state-machine logic; your first real USB run is the
hardware integration test.
