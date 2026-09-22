# Android Headless Mirror

**A REX Technologies product.** Your Android phone, in one window on your Windows PC.

Website: https://tochi-mba.github.io/Android_Headless_Mirror/

Plug the phone in and it appears on screen. The phone's own display stays off, so it does not
burn battery or react to stray touches while you use it from the PC. Everything lives in a single
window: the mirror, the controls, the phone settings and the app settings. No floating toolbars,
no separate settings windows.

REX is designed for Android devices supported by ADB and scrcpy. Current hardware testing is
primarily on a Samsung Galaxy S21 Ultra (SM-G998B) on Windows 11, so other phones and OEM builds
may expose different encoder, settings or lock-screen behaviour.

## What you get

- **One window.** The mirror fills the left; a side panel holds Controls, Phone, Settings and Info.
- **Automatic.** The app waits in the tray, opens when an authorised phone connects, restarts the
  mirror if it crashes, and stays closed after you stop it until the phone reconnects.
- **Real gestures.** Two fingers on a Windows Precision Touchpad become two fingers on the phone:
  pinch, rotate and pan exactly like on the glass (TikTok included). Hold **Alt** to zoom or pan the
  PC view instead; **Alt + wheel** and **Alt + drag** do the same with a mouse. The zoom is a true
  scaled surface, so clicks always land where you see them, and a small navigator shows where you are.
- **Phone controls.** Home, back, recents, power, screen off/on, volume, notifications, quick
  settings, rotation, clipboard both ways, screenshots.
- **Phone settings.** Brightness, timeout, forced rotation, dark mode, text size, animations, stay
  awake, Wi-Fi, mobile data, airplane mode, display size and density, plus a searchable browser for
  the raw Android settings provider (keys that would cut off ADB are protected).
- **Pattern-lock guide.** Some phones mirror their secure lock screen as black. For pattern locks the
  app draws a nine-dot guide, positioned from Android's own UI layout when available, with keyboard
  calibration as a fallback. The pattern itself is never stored or replayed.
- **Command line and agent mode.** `rex.exe` scripts everything; `rex agent ...` and `rex --json ...`
  emit exactly one JSON document and never prompt.

## Quick start

1. Clone or download this repository.
2. Run `REX.bat`. The first run prepares the app (from a release, or a local build if the .NET 10
   SDK is installed), then opens it.
3. Press **Install scrcpy** in the app. The official Genymobile release is downloaded and its SHA-256
   checksum is verified before anything is installed.
4. On the phone: Settings → About phone → tap *Build number* seven times, then Developer options →
   **USB debugging**.
5. Plug the phone in and tap **Allow** (tick *Always allow from this computer*).

The mirror opens by itself. Tick **Start with Windows** in Settings and it will wait in the tray after
every sign-in.

## Everyday use

| Want to…                       | Do this                                                       |
| ------------------------------ | ------------------------------------------------------------- |
| Zoom the PC view               | Alt + mouse wheel, or Alt + pinch on the touchpad             |
| Pan while zoomed               | Alt + drag, Alt + two-finger slide, or drag the navigator     |
| Pinch inside a phone app       | Two-finger pinch on the touchpad (no modifier)                |
| Turn the phone screen off/on   | The moon icon in the top bar / **Wake** in Controls           |
| Go fullscreen                  | F11; Esc returns to your previous window                       |
| Show fullscreen controls       | Move the pointer to the top edge; the translucent HUD fades after 3 seconds |
| Force phone orientation        | HUD → Rotation → Portrait / Landscape / Auto, or Ctrl+Alt+U / L / A |
| Turn only the PC view          | HUD → Rotation → View ↶ / ↷, or Ctrl+Alt+Left / Right          |
| Show or hide the pattern guide | Ctrl+Alt+P; Ctrl+Alt+C calibrates it with the arrow keys      |
| Stop the mirror                | **Stop mirror** in Controls; the app keeps waiting in the tray |
| Quit completely                | Tray icon → **Quit**                                          |

Closing the window keeps the app running in the tray (change this in Settings → Windows).

Fullscreen hides the title bar, taskbar, sidebar and status bar, and fits the complete phone display
without stretching or cropping it. Use Alt + wheel/pinch to zoom; **Fit** in the HUD restores the
whole screen. The HUD keeps Back, Home, Recents, Rotation and Exit within reach without reserving
screen space. Disconnecting returns to the normal window. Portrait and Landscape change Android's
rotation lock; **Auto** restores sensor rotation. Some Android apps enforce their own orientation;
the separate View controls can still turn the mirrored image.

## Command line

`REX.bat <command>` runs the CLI (`REX.bat` alone opens the app). Examples:

```text
rex status                         app, mirror and phone state
rex action sleep                   turn the phone screen off (rex action list)
rex zoom in | out | reset          PC-side zoom of the open mirror
rex screenshot                     save a PNG of the phone screen
rex phone set brightness 180       friendly phone settings (rex phone get)
rex android list global --filter animation
rex config set Mirror.MaxFps 90    app settings, with one-step undo (rex config restore)
rex autostart on | off
```

When several phones are connected, pass `--serial <SERIAL>`. The machine-readable contract for
scripts and coding agents is documented in [AGENTS.md](AGENTS.md).

## How it works

- `src/Rex.Mirror` is the WPF desktop app. It embeds scrcpy's window as a child of its own viewport,
  scales that surface for zoom, and draws the pattern guide and navigator on a transparent overlay.
- `src/Rex.Core` holds everything without a UI: configuration, ADB client, scrcpy arguments, the
  installer, state, geometry and the pipe protocol. The app and the CLI share it.
- `src/Rex.Cli` is `rex.exe`. Commands that need the live mirror talk to the app over a per-user
  named pipe; everything else uses ADB directly.
- `tests/` contains the xUnit suite, a fake `adb.exe` and a fake `scrcpy.exe` that let the whole
  app run end to end in CI without a phone, plus the Playwright checks for the website.
- `assets/make-icon.ps1` renders `assets/rex.ico` from the REX phone mark.

Configuration lives in `config.json` (list it with `rex config list`). Per-machine state such as
phone serials, lock-screen answers and window placement lives in `state.json`, which is never committed.

## Security notes

- USB debugging gives this PC full control of the phone. Only authorise computers you trust.
- Wireless ADB is off by default. USB remains the recovery path.
- The app never stores or injects a PIN, password or pattern. After a reboot some phones require the
  first unlock on the device itself; that is an Android boundary, not something the app bypasses.
- Nothing needs administrator rights. REX does not upload the mirrored screen, control data,
  lock information or phone content. Setup and updates may connect to GitHub to download REX and
  the official scrcpy distribution.

## Development

Run `REX.bat --source` to rebuild `tools/rex` from the current checkout and open that build instead
of substituting the latest GitHub release.

```powershell
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run test:pages
```

The test suite runs the real app against the fake phone tooling and saves screenshots under
`artifacts/screens`. CI does the same on `windows-latest` and publishes `rex-win-x64` as an artifact.
Tagging `v*` publishes a release archive with its checksum, which `REX.bat` then installs.
