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

### Guided REX CLI

1. On the Android device, enable Developer options and USB debugging.
2. Connect it to the Windows PC by USB.
3. Run `REX.bat`.
4. The REX first-run wizard checks whether scrcpy/ADB are installed, prepares anything missing, asks whether Windows startup should be enabled, discovers connected devices, records the device's lock-screen type, and guides the headless/mirror defaults.
5. On the first ADB connection, Android should show **Allow USB debugging?**
6. Select **Always allow from this computer** and press **Allow**.

The classic `SETUP_AND_START.bat` entry point remains available for people who prefer the original script-first setup.

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

The overlay uses the most accurate geometry source available, in this order:

1. **Exact Android pattern-cell bounds** from the current UI hierarchy, when the OEM exposes the nine virtual cells.
2. **Android `LockPatternView` bounds** from the current UI hierarchy. AOSP places the three pattern centers in each axis at the centers of the three equal cells, so the outer centers are at 1/6 and 5/6 of the runtime view bounds.
3. **Saved per-device calibration** when an OEM exposes a padded/non-standard pattern container or hides the widget completely.
4. **Estimated geometry** only as the final fallback.

Runtime discovery uses `uiautomator dump` only while the pattern guide is relevant. The hierarchy is read in memory and is not retained.

The guide itself:

- is a transparent, always-on-top Windows overlay aligned to the scrcpy **client area**;
- is click-through + no-activate, so mouse input continues to scrcpy underneath;
- follows scrcpy across move/resize and mixed-DPI/multi-monitor layouts;
- maps Android screen coordinates into the fitted scrcpy video area, including letterboxing and orientation changes;
- tries to show automatically when generic Android keyguard signals report a locked device;
- can be shown manually with **Ctrl+Alt+P** when OEM keyguard reporting is unreliable.

### Calibration fallback

Press **Ctrl+Alt+C** while the matching scrcpy window is focused to enter calibration mode.

Calibration never requires touching the phone. Use only the PC keyboard:

- **Arrow keys** — move the grid.
- **Shift + arrows** — resize the grid.
- **Ctrl + arrows** — fine 1-pixel adjustment.
- **Enter** — save calibration for this ADB serial.
- **Esc** — cancel without saving.
- **R** — remove the saved calibration and return to automatic discovery/fallback.

Saved calibration contains only four normalized geometry numbers (left/top/right/bottom) for that phone. It does not contain the pattern path.

### Pattern privacy

The overlay never persists or replays the unlock credential:

- it does **not** call ADB touch-injection commands;
- it does **not** store, log, transmit or replay the gesture path;
- optional calibration stores only the pattern-grid rectangle, never the unlock sequence;
- scrcpy remains the only input path;
- the optional cursor trail exists only in memory and is cleared shortly after the drag ends;
- `state.json` stores only the device serial and the selected mode (`pattern`, `other`, or `none`).

If a phone changes lock type later, run:

```
RESET_LOCK_SCREEN_CHOICES.bat
```

You can reset one serial or all saved lock-screen choices; matching saved pattern calibration is cleared at the same time and the next connection prompts again.

## Mirror controls

Android Headless Mirror adds a small always-on-top toolbar to each active mirror.

### Native touchpad pinch

On **Windows 11 with a Precision Touchpad**, two-finger touchpad gestures are read through the Windows Precision Touchpad pointer API:

- **Pinch/spread with two fingers** → Android receives a real two-finger pinch/rotate gesture through scrcpy.
- **Hold physical Ctrl + pinch/spread** → zoom the **PC mirror frame only**. Android receives no pinch.
- Host zoom persists after Ctrl is released.
- When host zoom is not 100%, the toolbar shows **Reset zoom**.

scrcpy is forced to SDK mouse mode so its virtual-finger multitouch path is always available to the bridge.

On Windows versions/hardware where the Precision Touchpad API is unavailable, normal mirroring remains available and scrcpy's **Ctrl + left-drag** pinch simulation remains the compatibility fallback.

### Sleep phone

The toolbar's **Sleep phone** button sends scrcpy's own “turn device screen off while keeping mirroring active” shortcut. It can be pressed again whenever the physical phone display has been woken; the PC mirror continues operating normally.

### Mouse-only host zoom

For mouse users, **Ctrl + mouse wheel** also controls the PC-only frame zoom. This is separate from Android pinch-to-zoom.

### Control Center

The toolbar's **Controls** button opens the Windows Control Center for the currently mirrored device. It is organized by responsibility rather than exposing one giant list:

- **Controls** — runtime scrcpy actions: fullscreen, fit, pixel-perfect, display rotation/flip, pause/resume, capture reset, FPS counter, Home/Back/Recent Apps/Menu, power, sleep/wake, Android orientation request, notification/Quick Settings panels, volume, clipboard actions, keyboard settings, host-zoom reset and screenshots.
- **PC / mirror settings** — wrapper/session behavior, video quality, codec, audio, recording-on-start, touchpad behavior, host zoom, pattern-guide behavior, wireless ADB and advanced raw scrcpy arguments.
- **<device> settings** — friendly ADB-backed controls for brightness, timeout, auto-rotate, font scale, show touches, stay-awake, animation scales, dark mode, Wi-Fi/mobile-data/airplane commands and display size/density overrides.
- **Advanced Android** — live enumeration of the connected phone's system, secure and global Settings Provider namespaces with search, read/write/delete and risk labels.
- **Diagnostics** — device identity, Android/API version, ADB state, capability probes and recent command status.

The Advanced Android page is intentionally **runtime-driven**. It does not assume that every OEM/version exposes the same keys. Android Headless Mirror asks that phone what keys exist and displays the result.

Some keys are protected by the wrapper (adb_enabled, development_settings_enabled, android_id and device identity fields) because changing them casually could sever the headless recovery path or mutate identity. Other sensitive/advanced changes require confirmation and still surface the exact Android/OEM permission failure if the shell user is not allowed to modify them.

## REX CLI

`REX.bat` is the single terminal entry point. It bootstraps the self-contained Windows CLI and then opens a REX-branded Spectre.Console application.

### First-run wizard

The first run is state-aware instead of showing a fixed questionnaire. REX checks the current package first, then guides only the work that is needed:

- install/verify scrcpy + ADB;
- choose whether Android Headless Mirror starts at Windows sign-in;
- discover attached Android devices and explain unauthorized/offline states;
- choose **Pattern**, **PIN/password/biometric/other**, **No screen lock**, or ask later for the selected device;
- choose physical-screen-off and USB stay-awake behavior;
- offer Precision Touchpad gestures when the Windows capability is relevant;
- choose whether the GUI Control Center opens automatically;
- optionally start the supervisor immediately.

After setup, Windows also gets a **REX** desktop shortcut. It launches `REX.bat smart` and behaves contextually:

- setup incomplete -> open the guided first-run wizard;
- mirror already running -> focus the existing mirror instead of starting a duplicate;
- authorized Android device connected + mirror stopped -> explicitly start the system and mirror;
- no authorized Android device + supervisor stopped -> start the supervisor and open a waiting/status experience;
- no authorized Android device + supervisor already running -> keep the existing supervisor and avoid a duplicate;
- persistent OFF is cleared because clicking the REX shortcut is itself an explicit start action.

After setup, the same CLI becomes the day-to-day workspace:

- **Runtime controls** — scrcpy controls plus PC-only host zoom in/out/reset.
- **PC / mirror settings** — categorized common settings plus an **All settings browser** over every leaf in `config.json`.
- **Device settings** — friendly Android settings using the same ADB backend as the GUI.
- **Advanced Android** — live `system`, `secure` and `global` Settings Provider browsing/search/write/delete with protected-key guardrails.
- **Control Center** — opens the per-device WPF UI without blocking the terminal.
- **Diagnostics**, **setup/repair**, **Windows startup**, **captures**, persistent **STOP**, and refresh/status.

### Scriptable commands

The interactive app is optional. The same executable supports automation.

For coding agents and other automation, use the prompt-free machine interface:

```text
REX.bat --plain capabilities
REX.bat --plain status
REX.bat --plain devices
REX.bat --plain smart
```

`--plain` and `--json` are aliases. They emit one versioned JSON document, no Spectre/ANSI UI, and never prompt. See `AGENTS.md` for the shell contract.

Human-readable commands remain available:

```text
rex status
rex devices
rex start
rex stop
rex controls --serial USB123
rex action sleep --serial USB123
rex mirror zoom-in --serial USB123
rex mirror reset-zoom --serial USB123
rex device set brightness 180 --serial USB123
rex device set animation-scale 0.5 --serial USB123
rex android list global --filter animation --serial USB123
rex android get secure some_key --serial USB123
rex android set system font_scale 1.15 --serial USB123
rex config list --filter MirrorChrome
rex config get MaxFps
rex config set MaxFps 90
rex config restore
rex autostart on
rex shortcut install
rex shortcut remove
rex screenshot --serial USB123
rex diagnostics
```

When exactly one authorized Android device is connected, `--serial` can be omitted. If multiple authorized devices are present, the scripted CLI requires an explicit serial rather than guessing.

### Safe config changes

REX preserves the type of the existing JSON setting when `config set` is used. Before each successful write it keeps the previous valid file as `config.json.rex-backup`. `rex config restore` swaps the current and previous versions, allowing a one-step undo/redo without hand-editing JSON.

### CLI distribution

Release builds publish `rex.exe` as a .NET 8 **self-contained, single-file Windows executable**. `REX.bat`:

1. uses the existing local executable when present;
2. otherwise looks for `rex-win-x64.zip` plus its SHA-256 checksum in the latest GitHub release and verifies the archive before extraction;
3. when no matching release asset exists, can build the CLI locally if a .NET 8 SDK is installed.

The PowerShell/ADB/scrcpy implementation remains shared with the GUI; the CLI is a presentation/orchestration layer rather than a second Android-control implementation.

### Screenshots and recording

- **Save screenshot** uses adb exec-out screencap -p and writes under captures/screenshots/ by default.
- **Record on next session** uses scrcpy's native recording pipeline and writes under captures/recordings/ by default.
- scrcpy 4.1 does not provide a dynamic start/stop-recording shortcut, so recording-on-start is explicitly presented as a next-session setting rather than pretending it is live.

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
- stops the mirror toolbar / host-zoom / touchpad-gesture sidecar;
- stops any open per-device Control Center;
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
- `PatternOverlay.AutoDiscoverGeometry`: discover the runtime Android pattern widget/cell bounds before using any estimate.
- `PatternOverlay.CalibrationHotkey`: enter keyboard-only calibration; default `Ctrl+Alt+C`.
- `PatternOverlay.CalibrationDirectory`: per-device normalized geometry files; ignored by Git.
- `MirrorChrome.SleepButton`: show the persistent **Sleep phone** toolbar action.
- `MirrorChrome.NativeTouchpadGestures`: enable Windows 11 Precision Touchpad gesture bridging when supported.
- `MirrorChrome.TouchpadPinchToAndroid`: map a native two-finger pinch/spread to Android multitouch.
- `MirrorChrome.CtrlTouchpadPinchToHostZoom`: map physical Ctrl + native touchpad pinch to PC-only frame zoom.
- `MirrorChrome.HostZoomEnabled`: enable persistent PC-only frame magnification and **Reset zoom**.
- `ControlCenter.Enabled`: enable the per-device Windows Control Center.
- `ControlCenter.ConfirmSensitiveDeviceWrites`: confirm advanced Android writes before execution.
- `ControlCenter.ScreenshotDirectory`: screenshot output directory.
- `ScrcpySession.VideoCodec`: `h264`, `h265` or `av1` for the next mirror session.
- `ScrcpySession.AudioEnabled` / `AudioCodec` / `AudioBufferMs` / `AudioDup`: scrcpy audio preferences.
- `ScrcpySession.Fullscreen` / `AlwaysOnTop` / `DisableScreensaver`: window behavior at session start.
- `ScrcpySession.RecordOnStart` / `RecordDirectory`: native scrcpy recording settings.
- `ExtraScrcpyArgs`: validated advanced argument escape hatch. Required wrapper invariants such as device serial, window title, SDK mouse mode and an interactive window cannot be overridden.



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

The repository includes Windows and browser CI that:

- validates JSON, XAML, JavaScript and PowerShell syntax;
- verifies launcher/script references and runtime-artifact ignore rules;
- runs static repository/package contracts;
- runs supervisor/overlay behavior under **PowerShell 7 and Windows PowerShell 5.1**;
- runs the real WPF Control Center against deterministic fake-device/fake-ADB backends and exercises every top-level tab and every named button flow;
- verifies scrcpy runtime action dispatch, PC settings validation/save/discard, friendly Android controls, advanced namespace search/write/delete/protected-key blocking, diagnostics and failure paths;
- renders all five Control Center tabs at a fixed 1040×760 target and gates them with screenshot color/contrast/visual-entropy/layout thresholds;
- runs Chromium desktop/mobile behavior, screenshot thresholds and axe accessibility checks for GitHub Pages;
- uploads WPF and Pages screenshot/metric artifacts on every CI run for visual diagnosis.

Hardware integration still requires a real Android device. CI deliberately proves deterministic command construction/UI behavior without claiming that every OEM grants every ADB setting permission.

## Security

USB debugging gives an authorised computer significant control over the connected Android device. Only permanently authorise computers you trust.

Wireless ADB exposes an additional network-access path and is therefore opt-in rather than enabled by default.
