# Android Headless Mirror

**A REX Technologies product.**

**Website:** https://tochi-mba.github.io/Android_Headless_Mirror/

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
- Auto-opens the mirror when an authorised phone is connected, with a 1-second detection interval by default.
- Wakes the device before launching scrcpy.
- Can turn the physical Android display off while keeping the PC mirror active.
- Keeps the active session alive using scrcpy `--keep-active`, and also uses `--stay-awake` for plugged-in USB sessions.
- Works with any authorised Android device; a preferred serial is only a preference when multiple ready devices are present.
- Can show a click-through 3×3 pattern guide over scrcpy when a secure lock screen renders black, without storing or replaying the pattern.
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

After authorisation, the hidden supervisor stays armed in the background. Connecting any authorised Android phone opens the mirror automatically (normally within the 1-second poll interval). It wakes the device, asks Android to dismiss the keyguard when authentication is not required, then launches scrcpy. If you manually close scrcpy while that phone remains connected, it stays closed until you disconnect and reconnect it.

## Headless day-to-day behavior

The design goal is that, after the one-time Android/ADB trust setup, normal use happens entirely from the Windows PC:

1. Windows signs in and starts the hidden supervisor.
2. An authorised Android phone is connected over USB.
3. The supervisor notices it within the configured poll interval (1 second by default).
4. The phone is woken.
5. Android receives a best-effort `wm dismiss-keyguard` request. Android only dismisses the keyguard when credentials are not required.
6. scrcpy opens automatically.
7. `--keep-active` periodically signals user activity so inactivity does not turn the session off.
8. On USB, `--stay-awake` additionally asks Android to stay awake while plugged in.
9. The physical display can remain off while the PC mirror stays active.

If the phone is securely locked but ADB remains available, scrcpy can still present the lock screen so you can authenticate from the PC rather than touching the phone.

### Security boundaries that cannot be automated safely

A stock Android device intentionally keeps some operations outside ADB automation:

- the **first ADB RSA authorisation** must be approved on the Android device;
- after a **full reboot**, some devices/OEM configurations require the first PIN/password unlock before USB data/ADB becomes available;
- if Android revokes this computer's ADB authorisation, the RSA prompt must be approved again;
- this project does **not** store or inject a PIN/password to bypass a secure keyguard.

These are Android security boundaries, not launcher failures. On devices where ADB remains available while locked, the mirrored lock screen can be used from the PC.

## Pattern-lock visual guide

Some Android/OEM combinations accept lock-screen touch input through scrcpy while rendering the secure lock screen as a black frame. For phones that use Android pattern unlock, Android Headless Mirror can place a **visual-only 3×3 guide** over the scrcpy client area.

The first time each authorised phone is seen, the PC asks about that device's lock setup:

- **No screen lock** → saves `none`; no overlay process is started.
- **Pattern lock** → saves `pattern`; the pattern guide is available for that phone.
- **PIN/password/biometric/other lock** → saves `other`; no pattern guide is started.
- **Cancel / ask later** → saves nothing and runs that session without the overlay.

The choice is stored **per ADB serial** in `state.json`, so different phones on the same PC can use different modes.

### How the pattern guide works

- The guide is a transparent, always-on-top Windows overlay aligned to the scrcpy **client area**.
- It uses click-through + no-activate window styles, so mouse input continues to scrcpy underneath.
- It follows scrcpy when the window moves/resizes and is DPI/multi-monitor aware.
- It tries to show automatically when generic Android keyguard signals report a locked device.
- OEM keyguard reporting is not perfectly consistent, so **Ctrl+Alt+P** manually toggles the guide for 20 seconds while the matching scrcpy window is focused.
- The 3×3 grid geometry is normalized to the fitted Android video area so letterboxing and landscape/portrait resizing are accounted for.
- Grid center, size, dot radius, opacity, polling intervals, trail duration and hotkey are configurable under `PatternOverlay` in `config.json`.

### Pattern privacy

The overlay never persists or replays the unlock credential:

- it does **not** call ADB touch-injection commands;
- it does **not** store, log, transmit or replay pattern coordinates;
- scrcpy remains the only input path;
- the optional cursor trail exists only in memory and is cleared shortly after the drag ends;
- `state.json` stores only the device serial and the selected mode (`pattern`, `other`, or `none`).

If a phone changes lock type later, run:

```
RESET_LOCK_SCREEN_CHOICES.bat
```

You can reset one serial or all saved lock-screen choices; the next connection prompts again.

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
- stops any pattern-guide sidecar started by this package;
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
- `StayAwakeWhenUsb`: use scrcpy `--stay-awake` while the active device is physically plugged in; scrcpy restores the previous Android setting when it closes.
- `KeepActiveDuringMirror`: use scrcpy `--keep-active` to periodically signal user activity while mirroring, including TCP/IP sessions.
- `DismissKeyguardWhenPossible`: ask Android to dismiss the keyguard before mirroring when authentication is not required; it does not bypass a secure PIN/password.
- `PowerOffOnClose`: optionally power the device display off when scrcpy closes.
- `MaxSize`: maximum encoded video dimension.
- `MaxFps`: frame-rate cap.
- `VideoBitRate`: video quality/bandwidth setting.
- `PreferredSerial`: optional fixed ADB serial.
- `RestartOnUnexpectedExit`: relaunch scrcpy after an unexpected failure while the supervisor is enabled.
- `PatternOverlay.Enabled`: enable per-device pattern-guide support.
- `PatternOverlay.PromptPerDevice`: ask once per new device about its lock-screen type.
- `PatternOverlay.AutoShowOnKeyguard`: automatically show the guide when OEM-tolerant keyguard signals report a lock screen.
- `PatternOverlay.ManualToggleHotkey`: manual fallback hotkey; default `Ctrl+Alt+P`.
- `PatternOverlay.GridCenterX` / `GridCenterY` / `GridSizeRelativeToWidth`: normalized grid geometry for OEM/device tuning.
- `PatternOverlay.ShowCursorTrail`: draw a temporary in-memory cursor trail while dragging over the guide.


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

The repository also includes Windows and browser CI that:

- validates JSON and PowerShell syntax;
- verifies launcher/script references;
- runs static package contracts;
- runs executable behavior tests under **PowerShell 7 and Windows PowerShell 5.1**;
- exercises overlay keyguard parsing, aspect-ratio geometry, hotkey parsing, state migration and reset behavior;
- runs Chromium desktop/mobile tests plus axe accessibility checks for GitHub Pages.

Hardware integration still requires a real Android device.

## Security

USB debugging gives an authorised computer significant control over the connected Android device. Only permanently authorise computers you trust.

Wireless ADB exposes an additional network-access path and is therefore opt-in rather than enabled by default.
