# Android Headless Mirror

**A REX Technologies product.** Your Android phone, in one window on your Windows PC.

Website and download: https://tochi-mba.github.io/Android_Headless_Mirror/

Plug the phone in and it appears on screen. The phone's own display stays off, so it does not
burn battery or react to stray touches while you use it from the PC. Everything lives in a single
window: the mirror, the controls, the phone settings and the app settings.

REX is designed for Android devices supported by ADB and scrcpy. Development and testing happen on
a Samsung Galaxy S21 Ultra (SM-G998B) on Windows 11, so other phones and OEM builds may expose
different encoder, settings or lock-screen behaviour.

## Install

1. Download the versioned **AndroidHeadlessMirror-Setup-x.y.z.exe** from the website (or the
   [latest release](https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest)) and run it.
   It installs for your user only, needs no administrator rights, and includes scrcpy. The
   installer is not code-signed yet: if SmartScreen appears, choose *More info → Run anyway*.
2. Choose *Start with Windows* (the app waits in the tray for your phone) and, if you like, a
   desktop shortcut.
3. The app opens and walks you through the phone side: turn on USB debugging (Settings → About
   phone → tap *Build number* seven times, then Developer options → USB debugging), plug the phone
   in and tap **Allow** with *Always allow from this computer* ticked.

From then on the mirror opens by itself whenever the phone is connected. Uninstall from Windows
Settings → Apps. Your settings live in `%LocalAppData%\REX\Android Headless Mirror` and stay
until you delete that folder.

## What you get

- **One window.** The mirror fills the left; a side panel holds Controls, Phone, Settings and Info.
  F11 is true fullscreen with a small HUD at the top edge.
- **Automatic.** The app waits in the tray, opens when an authorised phone connects, restarts the
  mirror if it crashes, and stays closed after you stop it until the phone reconnects.
- **Real gestures.** Two fingers on a Windows Precision Touchpad become two fingers on the phone:
  pinch, rotate and pan exactly like on the glass. Hold **Alt** to zoom or pan the PC view instead;
  **Alt + wheel** and **Alt + drag** do the same with a mouse. Zoom scales the real video surface,
  so clicks always land where you see them, and a small navigator shows where you are.
- **Phone controls.** Home, back, recents, power, screen off/on, volume, notifications, quick
  settings, rotation, clipboard both ways, screenshots.
- **Phone settings.** Brightness, timeout, forced rotation, dark mode, text size, animations, stay
  awake, Wi-Fi, mobile data, airplane mode, display size and density, plus a searchable browser for
  the raw Android settings provider (keys that would cut off ADB are protected).
- **Pattern-lock guide.** Some phones mirror their secure lock screen as black. For pattern locks the
  app draws a nine-dot guide, positioned from Android's own UI layout when available, with keyboard
  calibration as a fallback. The pattern itself is never stored or replayed.
- **Command line and agent mode.** `rex.exe` (on your PATH after installing) scripts everything;
  `rex agent ...` and `rex --json ...` emit exactly one JSON document and never prompt.

## Everyday use

| Want to…                       | Do this                                                        |
| ------------------------------ | -------------------------------------------------------------- |
| Zoom the PC view               | Alt + mouse wheel, or Alt + pinch on the touchpad              |
| Pan while zoomed               | Alt + drag, Alt + two-finger slide, or drag the navigator      |
| Pinch inside a phone app       | Two-finger pinch on the touchpad (no modifier)                 |
| Turn the phone screen off/on   | The moon icon in the top bar / **Wake** in Controls            |
| Go fullscreen                  | F11 (Esc leaves; the top edge reveals the HUD)                 |
| Force landscape / portrait     | Ctrl+Alt+L / Ctrl+Alt+U; Ctrl+Alt+A restores auto rotation     |
| Show or hide the pattern guide | Ctrl+Alt+P; Ctrl+Alt+C calibrates it with the arrow keys       |
| Stop the mirror                | **Stop mirror** in Controls; the app keeps waiting in the tray |
| Quit completely                | Tray icon → **Quit**                                           |

## Command line

```text
rex status                         app, mirror and phone state
rex action sleep                   turn the phone screen off (rex action list)
rex zoom in | out | reset          PC-side zoom of the open mirror
rex screenshot                     save a PNG of the phone screen
rex phone set brightness 180       friendly phone settings (rex phone get)
rex android list global --filter animation
rex config set Mirror.MaxFps 90    app settings, with one-step undo (rex config restore)
rex autostart on | off
rex setup                          download and verify the latest scrcpy release
```

When several phones are connected, pass `--serial <SERIAL>`. The machine-readable contract for
scripts and coding agents is documented in [AGENTS.md](AGENTS.md).

## How it works

- `src/Rex.Mirror` is the WPF desktop app. It embeds scrcpy's window as a child of its own viewport,
  scales that surface for zoom, and draws the pattern guide, navigator and fullscreen HUD on owned
  transparent windows.
- `src/Rex.Core` holds everything without a UI: configuration, ADB client, scrcpy arguments, the
  scrcpy installer, state, geometry and the pipe protocol. The app and the CLI share it.
- `src/Rex.Cli` is `rex.exe`. Commands that need the live mirror talk to the app over a per-user
  named pipe; everything else uses ADB directly.
- `installer/` builds a versioned `AndroidHeadlessMirror-Setup-x.y.z.exe` (Inno Setup): the published app and CLI,
  plus the latest scrcpy release, downloaded and SHA-256 verified by `rex setup`.
- `tests/` contains the xUnit suite, a fake `adb.exe` and a fake `scrcpy.exe` that let the whole
  app run end to end in CI without a phone, plus the Playwright checks for the website.

Installed, the app keeps `config.json`, `state.json`, logs and captures under
`%LocalAppData%\REX\Android Headless Mirror`. In a checkout, the repository folder is the root.

## Security notes

- USB debugging gives this PC full control of the phone. Only authorise computers you trust.
- Wireless ADB is off by default. USB remains the recovery path.
- The app never stores or injects a PIN, password or pattern. After a reboot some phones require the
  first unlock on the device itself; that is an Android boundary, not something the app bypasses.
- Nothing needs administrator rights. REX has no account or telemetry and does not upload the
  mirrored screen, control data, lock information or phone content. Setup and updates connect to
  GitHub only to download REX and the official scrcpy distribution.

## Development

```powershell
REX.bat                                   # builds tools\rex from this checkout on first use, then opens the app
REX.bat --build                           # rebuilds after a change
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run test:pages
./installer/build.ps1 -Version 2.0.1      # needs Inno Setup 6; writes dist/AndroidHeadlessMirror-Setup-2.0.1.exe
```

The test suite runs the real app against the fake phone tooling and saves screenshots under
`artifacts/screens`. CI builds, tests and packages the installer on every push; tagging `v*`
publishes the installer and its checksum as a GitHub release, which the website's download
button points at.
