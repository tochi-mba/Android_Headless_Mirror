# Android Headless Mirror

Android Headless Mirror is a Windows helper around [scrcpy](https://github.com/Genymobile/scrcpy) and ADB for people who want an Android phone to behave more like a device they can operate from their PC.

It is intended for **Android devices generally**. The current hardware-tested setup is:

- Samsung Galaxy S21 Ultra
- Model: SM-G998B
- Windows 10/11
- USB ADB + scrcpy

Other Android devices should work where standard ADB and scrcpy work, but they have not all been hardware-tested by this project.

## What it does

- Downloads the official Windows x64 scrcpy release during setup.
- Verifies the scrcpy ZIP against the release SHA-256 checksums before extraction.
- Prefers USB for the reliable recovery path.
- Waits for an authorised Android device instead of failing if it is disconnected or still booting.
- Wakes the device before launching scrcpy.
- Can turn the physical Android display off while keeping the PC mirror active.
- Keeps the device awake while connected over USB.
- Remembers a preferred ADB serial.
- Provides persistent **START / STOP** semantics.
- Can start automatically when Windows signs in.
- Includes diagnostics and rotating logs.
- Supports optional ADB-over-TCP/IP fallback, disabled by default.

## Quick start

1. On the Android device, enable Developer options and USB debugging.
2. Connect it to the Windows PC by USB.
3. Run `SETUP_AND_START.bat`.
4. On the first ADB connection, Android should show **Allow USB debugging?**
5. Select **Always allow from this computer** and press **Allow**.

After authorisation, the supervisor can reconnect to the device automatically.

## Start and stop behavior

The package has a persistent OFF state.

### Stop

Run:

```
STOP.bat
```

STOP:

- creates `stop.flag`;
- closes scrcpy;
- stops this package's background supervisor;
- leaves the shared Windows ADB server alone.

The `stop.flag` remains present, so Windows autostart will not resurrect the mirror.

### Start

Run:

```
START_NOW.bat
```

or:

```
START_WITH_LOG.bat
```

An explicit start removes `stop.flag` and launches the supervisor again.

`SETUP_AND_START.bat` also counts as an explicit start.

## Diagnostics

Run:

```
DIAGNOSTICS.bat
```

Diagnostics reports:

- ADB availability/version;
- scrcpy availability/version;
- whether the package is persistently OFF;
- whether the supervisor is running;
- whether scrcpy is running;
- connected ADB serials;
- ADB state: `device`, `unauthorized`, `offline`, or not detected;
- manufacturer/model when ADB is authorised;
- recent state/log information.

If ADB reports:

```
unauthorized
```

that means the computer is not yet authorised for ADB. It does **not** necessarily mean USB debugging is disabled. Unlock the Android device and approve the RSA prompt.

## Wireless ADB

Wireless fallback is deliberately disabled by default.

In `config.json`:

```json
"Wireless": {
  "Enabled": false,
  "Port": 5555,
  "EnableTcpipWhenUsbAvailable": false
}
```

If you explicitly enable it, the supervisor can bootstrap legacy `adb tcpip` while USB is available and remember candidate private IP addresses.

USB remains the recommended recovery path because legacy ADB TCP/IP normally does not survive a phone reboot.

## Configuration

Useful values in `config.json`:

- `TurnPhysicalScreenOff`: turn the physical device display off while mirrored.
- `StayAwakeWhenUsb`: request scrcpy's USB stay-awake behavior.
- `PowerOffOnClose`: optionally power the device display off when scrcpy closes.
- `MaxSize`: maximum encoded video dimension.
- `MaxFps`: frame-rate cap.
- `VideoBitRate`: video quality/bandwidth setting.
- `PreferredSerial`: optional fixed ADB serial.
- `RestartOnUnexpectedExit`: relaunch scrcpy after an unexpected failure while the supervisor is enabled.

## Samsung notes

On Samsung devices, security features such as Auto Blocker or USB restrictions while locked can interfere with ADB. These settings vary by One UI version.

Changing USB security settings reduces protection against someone with physical access to the phone, so only change them when you understand the trade-off.

After a full Android reboot, the device may require a first PIN/password unlock before some USB/data functionality becomes available. This project does not attempt to bypass Android's secure first-unlock behavior.

## Autostart

Setup installs a per-user Windows startup shortcut named:

```
Android Headless Mirror.lnk
```

Remove it with:

```
REMOVE_AUTOSTART.bat
```

The remover also cleans up the old `S21 Headless Mirror.lnk` name from earlier versions.

## Development

Tests:

```powershell
python tests\test_package.py
```

The repository also includes a Windows GitHub Actions workflow that:

- validates JSON;
- parses PowerShell files for syntax errors;
- verifies launcher/script references;
- runs the Python package tests.

Hardware integration still requires a real Android device.

## Security

USB debugging gives an authorised computer significant control over the connected Android device. Only permanently authorise computers you trust.

Wireless ADB exposes an additional network-access path and is therefore opt-in rather than enabled by default.
