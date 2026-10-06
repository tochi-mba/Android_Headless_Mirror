# AGENTS.md

Android Headless Mirror mirrors an Android phone into one Windows window (WPF) around scrcpy and
ADB. This file is the contract for coding agents and automation working in or against this repository.

Contents: [Machine interface](#machine-interface) · [Layout and paths](#layout-and-paths) ·
[Architecture](#architecture) · [Invariants](#invariants) · [Testing](#testing) ·
[Generated files](#generated-files-never-commit). People start with [README.md](README.md) and
[CONTRIBUTING.md](CONTRIBUTING.md); every command is described on the
[command line page](https://tochi-mba.github.io/Android_Headless_Mirror/cli.html).

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
`sound [0-100|up|down|mute|unmute|toggle]`, `app list [search] [--system]|open <name|package> [--fresh]|close <package>|info <package>|favourite <package> on|off`,
`push <files or folders...> [--to /sdcard/folder/] [--serial S]`,
`install <apk...> [--downgrade] [--grant] [--test] [--no-replace] [--serial S]`,
`screen [open <app> [--instead|--beside] [--size S] [--fresh]|app <app>|close]`,
`phones [beside [serial]|stop|switch|make-main]`, `encoders [--serial S]`,
`keys [set <action> <key|none> | reset [action]] [--browse]`,
`profile list|show <name>|apply <name>|save <name> [--groups A,B] [--all]|rename <old> <new>|delete <name>|export <name> <file>|import <file>`,
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
  MirrorActions         the single list of user actions, shortcuts and ADB-action dispatcher
  WorkGate              one worker at a time, and a close that waits for it to leave
  KeyChord/GlobalKey*   chords in words, which keys may work from anywhere, what the show-or-hide key does
  WindowKeys            the window's own keys, read from Shortcuts, matched with exactly their modifiers
  Sound*                the phone's sound on this PC: settings, the rules, fades, balance, where a level is kept
  App*/PackageName      the phone's apps: scrcpy's list read, searched and ordered, favourites, package checks
  ServerStartGate       one scrcpy server starting at a time, whoever starts it
  SettingsCatalogue     every config.json value and where it is changed; which ones apply at the next start
  SiteLinks             every web address the app and the CLI send people to
  WhatsNew              when the window says once, after an update, which version this is
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
  Services/Sound/*      the phone's sound on this PC: scrcpy's own Core Audio session (REX_FAKE_AUDIO in tests)
  Services/Files/*      sending files: the transfer queue's runner (FileSender) and File Explorer's Send to (REX_SENDTO_DIR in tests)
  Services/ProfileRunner  profiles in the app: applied by hand, key, tray or pipe, and by themselves (REX_FAKE_POWER in tests)
  Services/TestHooks    pipe commands for what a test cannot do for real (drag, drop); only with REX_TEST_HOOKS=1
  Views/*               the side-panel tabs and the guided first run (OnboardingView)
  Views/AppsPanel*      the Apps tab: rows made once per list and filtered while searching
  MainWindow.SecondScreen/Splitter  the second screen's view and session, the splitter, the marks over views
  Views/Settings/*      settings groups in separate controls
  Services/UrlOpener    opens the app's own https pages (REX_FAKE_BROWSER_LOG in tests)
src/Rex.Cli             rex.exe: human commands and MachineMode
  CliReference          every command once: rex help, capabilities and the site's command line page read it
tests/Rex.Tests         xUnit: unit, contract, end-to-end and UI-automation tests
  Support/AppProcess    drives a real RexMirror.exe: pipe, real input, screenshots, CLI
  Support/AppAutomation UI Automation over the window (x:Name is the AutomationId)
  Site/*                the writers of docs/settings, shortcuts, cli and changelog .html (SiteTests compares them)
  SiteScreenshots       the website's pictures from the real window (REX_SITE_SHOTS=1, CI)
tests/Rex.FakeAdb       deterministic adb.exe stand-in (scenario JSON, call log)
tests/Rex.FakeScrcpy    scrcpy.exe stand-in: a real window the app embeds
docs/                   GitHub Pages site, nine plain HTML pages; its download button points at the latest release asset
scripts/check-site.mjs  the check the Pages workflow and the site tests run before anything publishes
CHANGELOG.md            what changed in each version, newest first; changelog.html is made from it
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
- scrcpy is launched with `--shortcut-mod=rctrl` (not a setting: a Windows key posted to a window
  can open the Start menu, and an Alt is the PC view's or AltGr), `--mouse=sdk`, `--keyboard=uhid`, `--window-borderless`, `--background-color` (the app's ink, or
  black with the black backdrop, so a letterbox is invisible) and
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
- Keys from anywhere (`GlobalKeys`) are matched in the same keyboard hook, before the window's own
  keys and whatever is in front, and the hook only looks the key up and queues the work. Each
  needs Ctrl or is F13-F24 on its own (`GlobalKeyRules`), never takes AltGr or one of Windows' own
  keys, and nothing acts while a `ChordBox` records. The window's own keys come from
  `WindowKeys`, read from `Shortcuts`, and match only with exactly their modifiers.
- The phone's sound is only ever changed on this PC, through the scrcpy process's own audio
  session (`PhoneSound`), never on the phone: the phone's volume keys are separate actions with
  their own words and icons. The session is looked for only until it is found, the rules are
  checked four times a second, the fading timer runs only while fading and the meter only while
  the sound panel is open. Tests set `REX_FAKE_AUDIO`, and `AppProcess` always does.
- Files reach the phone only inside its shared storage (`TransferSettings.IsValidFolder`), and any
  phone path that passes through the phone's shell is quoted. While a drag is over the window the
  overlay is armed (one alpha step opaque), so a drop over the picture comes to this app rather
  than to scrcpy; it disarms on the drop, when the drag leaves the window, or when the button is
  up. A drop that reaches scrcpy anyway goes to the same folder (`--push-target`) and its lines are
  read into the same transfer list. Tests drag and drop through `drag`/`drop` pipe commands, which
  exist only with `REX_TEST_HOOKS=1`; `AppProcess` sets it.
- A second, different phone beside the main one is a full session of its own
  (`ScrcpyArguments.BuildOtherPhone`, port 27200) in a view the group panel lays out with the main
  phone (`TwoPhonesLayout`); `PhonePick` never shows one phone beside itself (hardware serial), and
  the main session never picks the phone beside (`SessionController.ShownBeside`). The side panel,
  keys and buttons act on `MainWindow.TargetPhone`, the phone whose view was used last;
  `ActionRouting.ForOtherPhone` says where an action goes while that is the phone beside.
- A profile is a list of config.json paths and values (`profiles/<name>.json`, order in `order.json`).
  Applying one is a single config write through `ConfigPaths` and sets only its own paths; settings
  it has that this version does not know are counted, never fatal. `ProfileBook` does the work for
  the app and the CLI alike; while the app runs, `rex profile` goes through the pipe. An automatic
  profile (fullscreen, then battery, then the phone's own) keeps what it changed in
  `profiles/.automatic.json` and puts back only the values still as it left them.
- The second screen is a copy's session (no cleanup, no audio, no power options) with a display of
  its own (`ScrcpyArguments.BuildScreen`): ports 27190 to 27199, `--new-display`, and `-x` when it
  follows the view, which then takes no window size. It is the group panel's second child, laid out
  with the phone by `ViewsLayout`; copies have no room while it is open. `SecondScreenPlan` decides
  (one retry, then the reason). While it has the keyboard, `ActionRouting` sends display keys to its
  own session as scrcpy shortcuts; the phone's view and session never change for it.
- The phone's apps are read with `scrcpy --list-apps --no-cleanup` (`AppLister`), once per
  connection after the mirror is up, never polled. Every scrcpy server start (the mirror, a copy,
  the app list) goes through `SessionController.ServerStart`, so two never start at once. A
  package reaches `am` or `pm` only after `PackageName` has checked it, as one argument; apps that
  came with the phone are never uninstalled. `rex app` goes through the running app when it shows
  that phone, and straight to the phone otherwise. The phone's video encoders are read the same
  way (`EncoderList`, `scrcpy --list-encoders --no-cleanup`), only when asked, through the gate.
- `KeyMap` is the only place a key is resolved: the window (`WindowKeys`), browse mode
  (`KeyboardBrowse`), `Shortcuts.Gesture`, tooltips and the Info tab all read `KeyMap.Current`,
  which only the app sets, from `Keys` in config.json (only what differs from how the app
  ships). A window key needs Ctrl, Alt or Win, or is F1 to F24 alone; browse keys are single keys;
  no key does two things.
- The Controls tab is built from `ControlsSettings` (`ControlsPanel.ApplyLayout`): each section
  sits in a host panel the settings show, hide and order, over the section's own show and hide
  rules; its grids hold the actions chosen. Every action has a picture (`ActionIcons.TileFor`), so
  any can be a tile; the fullscreen controls name four of them in words instead.
- The update check (`UpdateCheck`, `Services/UpdateChecker`) is opt-in: with
  `App.CheckForUpdates` off nothing is ever requested. On, it asks GitHub's latest release at most
  once a day; tests stand in for GitHub with `REX_FAKE_RELEASES`, which `AppProcess` always sets.
- A screenshot or recording is never written over: `CaptureName.Unique` counts {n} on, or adds
  " (2)", in the app and the command line alike. The verbose log (`LoggedProcessRunner`, on only
  with `Logging.Verbose`) never holds an `input` command's arguments: they would say where the
  screen was touched, a pattern unlock among them. The pipe's `quit` never asks first; the
  tray's Quit and closing the window do when `App.ConfirmQuit` says so.
- What the phone sends as the picture (`MirrorSettings` encoder, crop, capture orientation, angle,
  smoothing) reaches the main session and its copies; a second screen keeps only the encoder and
  smoothing, and the other phone only what is about this PC. Taps shown on the phone, the time
  limit, the start orientation and needing sound are the main session's. A mirror that ends at its
  time limit (`MirrorTimeLimit`) is a stop, never a crash to restart; `Session.RestartLimit` is how
  many restarts in a row the main session and the other phone get.
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
  upright picture and only as many as the width holds. A copy with no room (the window hidden or
  minimised, the phone on its side, too narrow a window) is stopped once that has lasted five
  seconds (`CopiesPlan.Room`, set by `MainWindow.UpdateRoom`) and started again as soon as there
  is room; more room counts at once, so resizing never makes a copy wait. Only the main session
  carries audio.
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
  Windows cannot replace a file while anything has it open, so `AtomicFile.Write` waits out a
  reader for about a second; a save that still fails is reported, never fatal.
- Only REX-owned mirror/session processes belong to the kill-on-close Job Object. Never assign the shared
  ADB server to it.
- A resource used on a worker thread is released only after that worker has left it
  (`WorkGate`): `LiveCapture` closes its gate and waits for the capture in flight before it
  disposes the graphics device, and leaves the objects to Windows rather than free them under it.
- A touch contact Windows refuses to lift or press is cancelled (`POINTER_FLAG_CANCELED`) before
  the next press, so one refused frame never leaves `TouchInjector` refused from then on.
- Low-level keyboard/mouse hooks may classify input and enqueue work only. Do not perform file I/O, ADB,
  process work, native resizing, or substantial layout/render work synchronously inside a hook callback.
- `app.manifest` is the source of truth for PerMonitorV2 DPI awareness. Code crossing WPF/Win32 geometry
  boundaries must be explicit about DIPs versus physical pixels.
- IPC requests stay current-user-only, size-bounded and deadline-controlled. Fire-and-forget server handlers
  must observe and log their own unexpected exceptions.
- Every configuration value must appear in `SettingsCatalogue.Controls`, `Elsewhere` or `NotYet`;
  `EveryValueInConfigHasAControl` enforces the complete mapping.
- Every ADB-backed action goes through `MirrorActions.RunAdbAsync`, so the app and both CLI modes dispatch it
  identically.
- Add each new fully testable Rex.Core source file to `tests/coverage-required.txt`; CI requires every listed
  executable line to remain covered.
- Tests wait for observable state rather than fixed sleeps, except where elapsed time itself is the
  behaviour under test or Windows input/compositor APIs expose no better signal.
- Every user-reachable control carries an `x:Name`, which WPF exposes as its AutomationId, so the
  UI tests can reach it. Anything new in the window needs a test in `AppUiTests`.
- WPF raises `ValueChanged` on sliders while XAML loads. Handlers that touch other controls must
  return until the panel has its host.
- The website and the docs cannot drift from the app. docs/settings.html, shortcuts.html, cli.html
  and changelog.html are made from SettingsCatalogue and the Settings tab's markup, `Shortcuts`,
  `CliReference` and CHANGELOG.md; `SiteTests` fails when a committed page differs, and
  `REX_WRITE_SITE=1` rewrites them. Every page shares index.html's header and footer, has one h1,
  is in the sitemap and passes `scripts/check-site.mjs`, axe and the no-sideways-scroll test.
- Every user-visible change adds a CHANGELOG.md entry; its newest heading is the version in
  Directory.Build.props.
- `rex help` and machine mode's `capabilities` read `CliReference`; a command added to either
  dispatcher without a reference entry fails `TheReferenceHasExactlyTheCommandsRexAccepts`.
- The app opens only https addresses from `SiteLinks`, through `UrlOpener`.
- No file over 1,000 lines. No dead code. Warnings are errors. Scripts are limited to `REX.bat`,
  `assets/make-icon.ps1`, `installer/build.ps1`, `installer/prepare-upgrade.ps1` and the Node
  scripts in `scripts/`.

## Testing

```powershell
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run check:site; npm run test:pages
./installer/build.ps1 -Version 0.0.0
$env:REX_WRITE_SITE = "1"; dotnet test --project tests/Rex.Tests/Rex.Tests.csproj -- --filter-class Rex.Tests.SiteTests   # rewrite the made pages
```

The end-to-end tests launch `RexMirror.exe` with `--root <temp package>` against `tests/Rex.FakeAdb`
and `tests/Rex.FakeScrcpy`, drive it over the pipe (`REX_PIPE_NAME`), and save screenshots to
`artifacts/screens`. Add a test for every user-visible change; CI is the release gate and also
compiles the installer. A successful `main` run publishes `AndroidHeadlessMirror-Setup.exe`
plus its SHA-256 when the version in `Directory.Build.props` is new. Releases are immutable; tags
and manual dispatch remain recovery paths, not the normal delivery path.

## Generated files (never commit)

`tools/`, `dist/`, `artifacts/`, `state.json`, `logs/`, `captures/`, `config.json.rex-backup`, build output.
