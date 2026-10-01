# Android Headless Mirror

**A REX Technologies product.** Your Android phone, in one window on your Windows PC.

**[Download](https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest/download/AndroidHeadlessMirror-Setup.exe)** ·
[Website](https://tochi-mba.github.io/Android_Headless_Mirror/) ·
[Features](https://tochi-mba.github.io/Android_Headless_Mirror/features.html) ·
[Guide](https://tochi-mba.github.io/Android_Headless_Mirror/guide.html) ·
[Shortcuts](https://tochi-mba.github.io/Android_Headless_Mirror/shortcuts.html) ·
[Every setting](https://tochi-mba.github.io/Android_Headless_Mirror/settings.html) ·
[Command line](https://tochi-mba.github.io/Android_Headless_Mirror/cli.html) ·
[Help](https://tochi-mba.github.io/Android_Headless_Mirror/help.html) ·
[What's new](CHANGELOG.md)

Plug the phone in and it appears on screen. The phone's own display stays off, so it does not
burn battery or react to stray touches while you use it from the PC. Everything lives in a single
window: the mirror, the controls, the phone's settings and the app's.

REX is designed for Android devices supported by ADB and scrcpy. Development and testing happen on
a Samsung Galaxy S21 Ultra (SM-G998B) on Windows 11, so other phones and OEM builds may expose
different encoder, settings or lock-screen behaviour.

## Install

1. Download **AndroidHeadlessMirror-Setup.exe** and run it. It installs for your user only, needs
   no administrator rights, and includes scrcpy. It is not code-signed yet: if SmartScreen appears,
   choose *More info → Run anyway*.
2. Choose *Start with Windows* (the app waits in the tray for your phone) and, if you like, a
   desktop shortcut.
3. The app walks you through the phone side: turn on USB debugging (Settings → About phone → tap
   *Build number* seven times, then Developer options → USB debugging), plug the phone in and tap
   **Allow** with *Always allow from this computer* ticked.

From then on the mirror opens by itself whenever the phone is connected. Uninstall from Windows
Settings → Apps; your settings stay in `%LocalAppData%\REX\Android Headless Mirror` until you
delete that folder.

## Highlights

- **One window, true fullscreen.** A side panel holds Controls, Apps, Phone, Settings and Info. F11 fills
  the display, and the controls become a small bar you drag wherever you like.
- **Real gestures.** Two fingers on a precision touchpad are two fingers on the phone. Hold Alt and
  the same gestures zoom and pan the view on this PC instead, so clicks always land where you see.
- **A keyboard is enough.** Every key types as on a plugged-in keyboard, and browse mode
  (Ctrl+Alt+K) moves through a feed with the arrow keys.
- **The phone's sound, your level.** The sound button in the top bar sets how loud the phone plays on
  this PC, remembered for each phone, and it can mute itself while the window is away.
- **Every app, one click away.** The Apps tab lists the phone's apps by name; type to find one,
  star the ones you use and open them with Ctrl+Alt+Shift+1 to 9.
- **Keys from anywhere.** Ctrl+Alt+M brings the mirror up from any app and sends it back to the
  tray, and any action can have a key of its own that works while the window is hidden.
- **Copies side by side.** Ctrl+Alt+N adds another live view of the phone; copies nobody can see
  pause themselves, and only the main view plays sound.
- **Yours to shape.** Every setting lives in config.json, previews as you change it, and is
  [explained on the website](https://tochi-mba.github.io/Android_Headless_Mirror/settings.html).

All of it, with the settings that shape each part, is on the
[features page](https://tochi-mba.github.io/Android_Headless_Mirror/features.html).

## Everyday use

| Want to…                        | Do this                                                          |
| ------------------------------- | ---------------------------------------------------------------- |
| Zoom the PC view                | Alt + wheel, or Alt + pinch on the touchpad                      |
| Pan while zoomed                | Alt + drag, or drag the navigator                                |
| Pinch inside a phone app        | Two fingers on the touchpad, with no key held                    |
| Turn the phone screen off or on | The moon in the top bar, or **Wake** in Controls                 |
| Go fullscreen                   | F11; Esc leaves                                                  |
| Force landscape or portrait     | Ctrl+Alt+L or Ctrl+Alt+U; Ctrl+Alt+A lets the phone decide       |
| Show the pattern guide          | Ctrl+Alt+P; Ctrl+Alt+C lines it up with the arrow keys           |
| Jump between the side tabs      | Ctrl+Alt+1 to Ctrl+Alt+5; Ctrl+Alt+B hides the panel             |
| Open an app on the phone        | Ctrl+Alt+2, type its name, Enter; favourites on Ctrl+Alt+Shift+1 to 9 |
| Quieter, louder or mute on this PC | Ctrl+Alt+PageDown, Ctrl+Alt+PageUp, Ctrl+Alt+Shift+M, or the sound button |
| Take a screenshot               | Ctrl+Alt+S, or the camera in the top bar                         |
| Browse a feed without the mouse | Ctrl+Alt+K, then the arrow keys, Enter, L, M and Backspace       |
| See the phone twice or more     | Ctrl+Alt+N adds a copy; Ctrl+Alt+W closes the last one           |
| Go back, or show recent apps    | Ctrl+Alt+Backspace, or Ctrl+Alt+R                                |
| Bring the mirror up from any app | Ctrl+Alt+M, from anywhere; press it again to hide it            |
| See the window explained again  | F1, or Info → **Take the tour**                                  |
| Find a setting                  | The search box at the top of Settings                            |
| Get help                        | Info → **Help**, or the [help page](https://tochi-mba.github.io/Android_Headless_Mirror/help.html) |
| Quit completely                 | Tray icon → **Quit**                                             |

Every key and gesture is on the
[shortcuts page](https://tochi-mba.github.io/Android_Headless_Mirror/shortcuts.html).

## Command line

```text
rex status                         app, mirror and phone state
rex action sleep                   turn the phone screen off (rex action list)
rex zoom in | out | reset          PC-side zoom of the open mirror
rex screenshot                     save a picture of the phone screen
rex sound 40                       the phone's sound on this PC (up, down, mute)
rex phone set brightness 180       friendly phone settings (rex phone list)
rex config set Mirror.MaxFps 90    app settings, with one-step undo (rex config restore)
rex diagnostics                    everything needed to report a problem
```

`rex help` lists every command, and the
[command line page](https://tochi-mba.github.io/Android_Headless_Mirror/cli.html) has an example
of each. With more than one phone connected, pass `--serial <SERIAL>`. Scripts and coding agents
use `rex agent <command>`, which writes exactly one JSON document; the contract is in
[AGENTS.md](AGENTS.md).

## When something is not right

The [help page](https://tochi-mba.github.io/Android_Headless_Mirror/help.html) goes through it by
symptom. In short:

- **No phone found.** Try another cable (charge-only cables carry no data) and a port on the PC
  itself, unlock the phone and allow USB debugging.
- **Windows cannot read the phone.** The app notices and offers **Repair USB driver** or
  **Fix USB**, with one administrator approval, and can fix it by itself from then on.
- **Anything else.** Info → **Copy diagnostics**, then Info → **Report a problem**.

## Security

USB debugging gives this PC full control of the phone, so only allow computers you trust. The app
never stores or types a PIN, password or pattern, has no account or telemetry, and needs
administrator approval only for the USB repairs. More in [SECURITY.md](SECURITY.md).

## How it works

- `src/Rex.Mirror` is the WPF desktop app. It embeds scrcpy's window inside its own, scales it for
  zoom, and draws the soft background, pattern guide, navigator and fullscreen controls around it.
- `src/Rex.Core` holds everything without a window: settings, the ADB client, scrcpy arguments,
  the scrcpy installer, state, geometry and the pipe protocol. The app and the command line share it.
- `src/Rex.Cli` is `rex.exe`. Commands that need the open window talk to the app over a per-user
  named pipe; the rest use ADB directly.
- `installer/` builds the installer; `docs/` is the website; `tests/` runs the whole app end to end
  in CI against a fake `adb.exe` and a fake `scrcpy.exe`.

Building, testing and the rules every change follows are in [CONTRIBUTING.md](CONTRIBUTING.md).
