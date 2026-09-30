# AGENTS.md

Android Headless Mirror mirrors an Android phone into one Windows window (WPF) around scrcpy and
ADB. This file is the contract for coding agents and automation working in or against this repository.

## Machine interface

Use the CLI in machine mode. All three spellings are equivalent and select the same protocol:

```bat
rex agent <command> [args]
rex --json <command> [args]
rex <command> --json
```

`rex` is on the PATH after installing; in a checkout use `REX.bat agent <command>` instead.

The machine interface never prompts, writes exactly one JSON document to stdout, contains no ANSI
sequences, exits 0 on success and 1 on failure, and reports `protocolVersion` 2:

```json
{"ok":true,"protocolVersion":2,"command":"status","data":{...}}
{"ok":false,"protocolVersion":2,"command":"action","error":{"type":"InvalidOperationException","message":"..."}}
```

Discover the live contract first:

```bat
REX.bat agent capabilities
```

Commands: `capabilities`, `status`, `devices`, `diagnostics`,
`usb [list|status|repair|enable-auto-repair|disable-auto-repair|run-auto-repair] [--dry-run]`, `open`, `stop`, `quit`, `action <id>`,
`zoom <in|out|reset>`, `screenshot`, `phone get|set <setting> <value>`,
`android list|get|set|delete <system|secure|global> [key] [value] [--filter text]`,
`config list|get|set|restore`, `autostart on|off`, `lock-mode <serial> <pattern|other|none>`,
`reset-lock [serial|ALL]`.

Rules:

- Omit `--serial` only when exactly one authorised phone is connected; otherwise pass it. The CLI
  never guesses between phones.
- ADB-backed actions (home, back, volume, notifications...) work without the app. scrcpy-backed
  actions (screen off, rotate, pause, clipboard) and `zoom` need the app running; use `open` first.
- `android set` refuses the protected keys that could cut off ADB (`adb_enabled`,
  `development_settings_enabled`, `android_id`, `bluetooth_address`, `adb_wifi_enabled`).
- `config set` keeps the type of the existing value and writes `config.json.rex-backup` first;
  `config restore` swaps them back.
- `usb repair`, `usb enable-auto-repair` and `usb disable-auto-repair` change the PC (HKLM device
  registrations, pnputil, Task Scheduler), so in machine mode they require an already elevated
  shell and fail otherwise; the human CLI and the app go through the UAC prompt. `--dry-run` says
  what they would do without either. `usb run-auto-repair` needs no elevation.

## Layout and paths

Two layouts exist and `AppPaths` resolves them:

- **Installed** (`installer/`): executables in `%LocalAppData%\Programs\Android Headless Mirror`
  with scrcpy under `scrcpy\`; data (`config.json`, `state.json`, `logs`, `captures`, app-installed
  scrcpy updates under `tools\scrcpy`) in `%LocalAppData%\REX\Android Headless Mirror`.
- **Checkout**: the repository folder (config.json next to REX.bat) is the root for both.

`REX_ROOT` overrides the root; `REX_ADB_PATH` + `REX_SCRCPY_PATH` override tool discovery.

## Architecture

```
REX.bat                 developer launcher: builds tools\rex with the .NET SDK, opens the app or runs rex.exe
installer/              Inno Setup script + build.ps1 (publish, bundle verified scrcpy, compile, checksum)
src/Rex.Core            UI-free library shared by the app and the CLI
  AppPaths              root discovery (checkout vs installed) and every derived path
  RexConfig/ConfigFile  typed config.json (schema 2) with migration from the schema-1 keys
  ConfigStore           dotted-path access to config.json for the CLI
  AdbClient/AdbParsing  every ADB call, quoting, output parsing, friendly settings
  UsbAdbInterfaces      Windows ADB interfaces adb cannot see, and their repair
  UsbProblems           phones Windows could not read over USB (pure classifier over a thin CfgMgr32 scan)
  UsbRecoveryPolicy     when to repair and what the notice says: episodes, delays, rate limits
  UsbRepairPlan         the prompted repair: restart failed nodes, then remove and rescan what is left
  UsbAutoRepairTask     the no-prompt repair: a fixed System32 pnputil task (WindowsTaskScheduler)
  UsbSystem             every USB-touching dependency in one place, with the test seams
  ScrcpyArguments       the scrcpy command line for an embedded session (and for a copy of it)
  CopiesLayout/Plan     copies of the phone: how many fit, their cells, and which to start or stop next
  ScrcpyInstaller       verified download of the official scrcpy release; ToolLocator finds the newest copy
  StateStore            state.json: phones, lock-screen answers, calibration, UI state
  PatternGeometry       pattern-guide geometry (pure functions)
  ZoomMath              zoom/pan geometry (pure functions)
  AmbientLayout         soft-background geometry: image size, margins, tint hue, navigator corner
  Ipc                   pipe protocol between rex.exe and the app
  MirrorActions         the single list of user actions and their scrcpy shortcuts
src/Rex.Mirror          WPF app (RexMirror.exe)
  Mirror/MirrorHost     HwndHost that embeds scrcpy and scales it for zoom
  Mirror/MirrorGroupPanel lays out the main view and its copies side by side
  Mirror/OverlayWindow  transparent layer: soft background, pattern guide, navigator, touchpad receiver
  Mirror/LiveCapture    one downscaled frame of the on-screen mirror surface for the soft background
  Mirror/FullscreenHud* the compact fullscreen HUD in its own non-activating window
  Mirror/TouchpadBridge Precision Touchpad contacts → phone touch (or Alt → host zoom/pan)
  Mirror/PatternGuide   keyguard polling, geometry discovery, calibration
  Session/*             supervisor: device watching, scrcpy lifecycle, actions, copies (CopiesController)
  Services/*            composition root, pipe server, command router, tray icon, UsbDoctor
  Views/*               the side-panel tabs and the guided first run (OnboardingView)
src/Rex.Cli             rex.exe: human commands and MachineMode
tests/Rex.Tests         xUnit: unit, contract, end-to-end and UI-automation tests
  Support/AppProcess    drives a real RexMirror.exe: pipe, real input, screenshots, CLI
  Support/AppAutomation UI Automation over the window (x:Name is the AutomationId)
tests/Rex.FakeAdb       deterministic adb.exe stand-in (scenario JSON, call log)
tests/Rex.FakeScrcpy    scrcpy.exe stand-in: a real window the app embeds
docs/                   GitHub Pages site; its download button points at the latest release asset
```

## Invariants

- One window. Nothing floats over the mirror except the transparent overlay it owns.
- The first run is guided inside the window (OnboardingView) until a phone has been mirrored once;
  it never blocks the mirror from opening.
- F11 uses the full monitor bounds. Its compact HUD lives in the owned overlay and fades when idle;
  leaving fullscreen restores window placement. Phone orientation actions are distinct from PC view rotation.
- The HUD is dragged by its bar and dropped anywhere; the position is a fraction of the mirror area,
  so it survives resizing and rotation, and reaching for it brings it back where it was left.
  Its dragging is driven from the window's own messages: the HUD never takes activation, and WPF
  does not route input through the element tree of a window that is never active. Buttons are
  excluded by where they were laid out, not by hit testing, for the same reason.
- Every control whose content is not already the words a person would say carries an
  `AutomationProperties.Name`: an icon button without one is announced as its own path data, and a
  slider without one as a bare number. The window is expected to be drivable with Narrator alone.
- Shortcuts come from `Shortcuts` in Rex.Core, never written out a second time. The Info panel, the
  tooltips, the pattern guide's own label and the website's table all read it, and a repository test
  fails if the site and the registry disagree.
- The first-run tour lives in `Views/Tour.cs` (the steps), `Views/TourOverlay` (the spotlight) and
  `TourLayout` in Rex.Core (where the callout goes). One-time hints come from `Tips` and appear in
  the notice bar, never in a second mechanism of their own. Tests opt into the tour with
  `new TestPackage(showTour: true)`; it is off by default because it covers the window on purpose.
- Asking before something risky is `MainWindow.ConfirmAsync`, in the window and in the app's voice.
  A system message box is only for failures that happen before a window exists.
- High Contrast swaps `HighContrast.xaml` over the palette, and the soft background stands down.
  Any new palette key needs an answer in both files or a repository test fails.
- Two fingers over one of the app's own lists scroll that list. The mirror is a child window that
  otherwise takes the whole gesture, so anything scrollable the pointer can rest on has to be
  offered to `TouchpadBridge.PanelAt` or it will be scrolled on the phone instead.
- scrcpy is launched with `--shortcut-mod=rctrl`, `--mouse=sdk`, `--keyboard=uhid`,
  `--window-borderless`, `--background-color` (the app's ink, so a letterbox is invisible) and
  `--no-window-aspect-ratio-lock`; `Mirror.ExtraArgs` cannot override these
  and is validated wherever it is written (settings panel, `config set`, `Normalize`). If Android
  denies UHID, the session retries once with `--keyboard=sdk --raw-key-events` and remembers it in
  the device profile (`CompatibilityKeyboard`) so later sessions start there; `Mirror.CompatibilityKeyboard`
  forces it for every phone. Never fall back to plain SDK mode because an embedded SDL window loses
  its digit and punctuation text events. `ScrcpyArguments.KeyboardModeFor` is the one place the choice is made.
- Keyboard-only phone gestures (`MirrorActions.Gestures`) go through `KeyboardTouch`: one-finger
  Windows touch injection over the visible mirror surface, or Android's own `input swipe`/`input tap`
  over ADB when there is no picture to touch (window hidden, injection refused). Browse mode
  (`KeyboardBrowse`, toggled by the `browse` action) is the only time plain keys are taken from the
  phone, and only while focus is not in one of the app's own controls. Keep ordinary keys for typing
  otherwise, keep AltGr out of the Ctrl+Alt hotkey path, and add every new chord or browse key to
  `Shortcuts` so the app and website continue to agree.
- Left Alt pressed on its own belongs to the PC view (zoom, pinch, pan). While it is held,
  `MirrorHost.HoldKeyboard` moves keyboard focus from scrcpy to the viewport, from inside the
  keyboard hook so it happens before Windows routes the key; otherwise the hardware keyboard shows
  Android's shortcut list for a held modifier. This focus change is the one synchronous thing the
  hook is allowed to do. Right Alt (AltGr) and Alt with Ctrl, Shift or Windows still reach the phone.
- The pattern guide asks Android where the pattern is every time the lock screen comes up. A saved
  calibration only applies in the orientation it was made in, and saving one without moving it
  clears it instead: an unmoved calibration would freeze the automatic placement for good.
- The picture's shape comes from scrcpy's own report of the video size (`INFO: Texture: WxH` on
  stdout, `ScrcpyProcess.VideoSizeChanged`). scrcpy's window size is only a fallback signal, and
  only counted against the size the app last gave it (`ChildShape`): a window that has not been
  laid out yet still has the shape it opened with, and once the video has been reported a window
  that changes size by itself is given its size back rather than believed.
- Zoom scales the embedded surface. Never reintroduce a magnifier or a second window for zoom.
- A launch setting belongs either to every session or to the main one alone
  (`ScrcpyArguments.Build`). How the picture is shown and how input reaches the phone (buffer,
  renderer, mouse buttons, hover, clipboard, paste) apply to every copy too. Anything that changes
  the phone or this PC and is put back when its session ends (screen timeout, screen saver), the
  audio, the app to start, recording and game controllers are the main session's alone. Options
  scrcpy refuses in combination are never passed together: `--no-key-repeat` only with the
  raw-key keyboard, `--audio-dup` only with a source it can duplicate. Values that are scrcpy's
  own defaults are left off the command line.
- Copies of the phone are extra scrcpy sessions, each embedded in its own `MirrorHost` beside the
  main one. The main view leads: zoom, pan, the navigator, the pattern guide and the soft
  background's source are its own, and every copy follows its zoom (`MirrorHost.Follow`). A point
  over a copy is mapped to the same spot on the main view (`MainWindow.OnMainView`) before it
  anchors a zoom or starts a touch. A copy passes `--no-cleanup --no-power-on --no-audio` and none
  of the power options, because each scrcpy session restores its own snapshot of the phone when it
  exits and would undo the main session's; it gets a port of its own (`ScrcpyArguments.CopyPort`)
  and records nothing. Copies start one at a time after the main picture is up (`CopiesPlan`): two
  sessions starting together race for the server upload and the port. Copies are only shown for an
  upright picture and only as many as the width holds; the rest keep running out of sight.
  A scrcpy shortcut that changes only the picture on this PC (turns, flips, pause, recapture:
  `ScrcpyShortcuts.AppliesToEveryView`) is sent to every copy too, and a copy opens with the main
  view's current `--display-orientation` (`DisplayOrientation`, composed as scrcpy composes it)
  and paused if the main view is. Anything that acts on the phone is sent once, to the main session.
- The soft background and the navigator picture are live copies of the on-screen mirror
  (`LiveCapture`), never phone screenshots: they must not add ADB traffic, and nothing else may poll
  the phone for pictures either. One capture feeds both. It goes through DXGI desktop duplication
  (`GpuCapture`): the region is cut and shrunk on the GPU with a mip chain and only the small
  picture is read back, and nothing is read while the desktop is unchanged. GDI copying is the
  fallback when the GPU path is refused, retried with a growing pause. The background is blurred
  small (`AmbientBlur`), so its cost does not grow with the window; never blur it at display size.
- The soft background is drawn by the main window (`AmbientView`, behind `MirrorHost`), not by the
  overlay. The viewport window is clipped to the picture with a window region so the margins show
  it. Moving it back into the overlay costs most of a core at 60 frames a second.
- The mirror overlay is a per-pixel-alpha window, which Windows redraws on the CPU whenever anything
  on it changes. Only small things that must sit over the phone belong there (pattern guide,
  navigator, touchpad receiver). Everything drawn there is compared against what it drew last and
  skipped when nothing moved, and no animation may run on it while its element is hidden: a WPF
  animation keeps the render loop awake whether or not anything can be seen.
- Every stock control the app puts on screen has a style in `Theme.xaml` (menus, lists, progress,
  splitter included); `RepositoryTests.NoControlIsLeftLookingLikeStockWindows` fails otherwise. The
  tray menu is Windows Forms and is painted by `TrayMenuRenderer`, whose colours must match the
  palette.
- The overlay covers the whole mirror area, which with copies is wider than the main view:
  anything drawn on it for the main view is placed with `MirrorHost.AreaSurfaceRect`.
- The window's own preferences (on top, the panel's side, the top bar's buttons, hints, the frame
  rate readout, connection notices) apply live in `MainWindow.ApplyWindowPreferences`. The panel
  changes sides by moving `SidebarColumn`, so its width, splitter and hiding behave the same on
  either side. scrcpy's frame rate counter is switched in the running session
  (`SessionController.ApplyFrameRateSetting`, the same shortcut as the counter action) and
  `--print-fps` is left out of `ScrcpyArguments.LaunchSettings`, so it never asks for a restart.
- Every visual choice the user can make lives in `config.json` and previews instantly:
  `AppHost.PreviewConfig` updates memory and debounces the write, `UpdateConfig` writes at once.
  The app also watches config.json, so `rex config set` applies to the running window.
- A phone that leaves ADB while mirroring is a disconnect, not a stop: return to Waiting and pick
  it up when it returns. Only an explicit stop, or repeated crashes with the phone still present,
  reach Stopped.
- Plain two-finger touchpad gestures go to the phone as real touch. Alt is the only host modifier.
- Any authorised phone can be used. A preferred serial is a preference, never a lock.
- The app never calls `adb kill-server`, never stores or injects unlock credentials, and needs admin only
  for the explicit USB repairs (`UsbAdbInterfaces`, `UsbRepairPlan`) and for setting up or removing
  the auto-repair task, always via the Windows prompt.
- The installer is per-user, so the app's own executables are writable by the user. Nothing may
  ever run them with elevated rights without a prompt: that would hand administrator rights to
  whatever replaced them. The one no-prompt path, `UsbAutoRepairTask`, runs only System32
  `pnputil.exe` with fixed arguments that name the generic failed-enumeration ids, as SYSTEM, with a
  security descriptor that lets interactive users start it and nobody but SYSTEM and Administrators
  change it. The app starts it only when the registered definition matches exactly
  (`UsbAutoRepairTask.IsCurrent`). Tests never reach the real devices, Task Scheduler or UAC:
  `REX_FAKE_USB_PROBLEMS` and `REX_FAKE_USB_REPAIR_LOG` replace them, and `AppProcess` always sets both.
- Before upgrading, the installer stops processes whose executable paths are inside the installation
  directory, including its bundled ADB server. It never sends a global `adb kill-server` command.
- Wireless ADB stays opt-in.
- scrcpy installs are never deleted while a copy may be running; a new version goes into a new folder
  and discovery picks the newest complete one.
- Persistence is transactional across processes: every config/state read-modify-write mutation must hold
  `CrossProcessFileLock`. `AtomicFile` provides crash-safe replacement, not concurrency control.
- Only REX-owned mirror/session processes belong to the kill-on-close Job Object. Never assign the shared
  ADB server to it.
- Low-level keyboard/mouse hooks may classify input and enqueue work only. Do not perform file I/O, ADB,
  process work, native resizing, or substantial layout/render work synchronously inside a hook callback.
- `app.manifest` is the source of truth for PerMonitorV2 DPI awareness. Code crossing WPF/Win32 geometry
  boundaries must be explicit about DIPs versus physical pixels.
- IPC requests stay current-user-only, size-bounded and deadline-controlled. Fire-and-forget server handlers
  must observe and log their own unexpected exceptions.
- Tests wait for observable state rather than fixed sleeps, except where elapsed time itself is the
  behaviour under test or Windows input/compositor APIs expose no better signal.
- Every user-reachable control carries an `x:Name`, which WPF exposes as its AutomationId, so the
  UI tests can reach it. Anything new in the window needs a test in `AppUiTests`.
- WPF raises `ValueChanged` on sliders while XAML loads. Handlers that touch other controls must
  return until the panel has its host.
- No file over 1,000 lines. No dead code. Warnings are errors. Scripts are limited to `REX.bat`,
  `assets/make-icon.ps1`, `installer/build.ps1` and `installer/prepare-upgrade.ps1`.

## Testing

```powershell
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run test:pages
./installer/build.ps1 -Version 0.0.0
```

The end-to-end tests launch `RexMirror.exe` with `--root <temp package>` against `tests/Rex.FakeAdb`
and `tests/Rex.FakeScrcpy`, drive it over the pipe (`REX_PIPE_NAME`), and save screenshots to
`artifacts/screens`. Add a test for every user-visible change; CI is the release gate and also
compiles the installer. A successful `main` run publishes `AndroidHeadlessMirror-Setup.exe`
plus its SHA-256 when the version in `Directory.Build.props` is new. Releases are immutable; tags
and manual dispatch remain recovery paths, not the normal delivery path.

## Generated files (never commit)

`tools/`, `dist/`, `artifacts/`, `state.json`, `logs/`, `captures/`, `config.json.rex-backup`, build output.
