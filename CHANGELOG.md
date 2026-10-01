# Changelog

What changed in each version of Android Headless Mirror, newest first. Every version listed here
was built and tested by CI; the installer for each published one is on the
[releases page](https://github.com/tochi-mba/Android_Headless_Mirror/releases).

## 2.6.0 - 2026-10-01

### Added

- An **Apps** tab lists every app the phone can open, by its own name: type to find one, click to
  open it, Shift+click to open it fresh. Favourites come first in your order, then the apps you
  opened last, then the rest; the apps that came with the phone have a group of their own when
  you want it, and a search always finds them.
- Each app's menu opens its info page, closes it, hides it from the list, copies its package
  name, and, for apps you installed, clears its data or uninstalls it after asking.
- Starred apps get tiles at the top of the Controls tab, and Ctrl+Alt+Shift+1 to 9 open them.
  They can sit on the fullscreen controls too.
- Settings has an **Apps** group: system apps, package names, the order, a list or tiles, how many
  recent apps to remember, where favourites show, opening apps fresh, and closing the apps opened
  from here when the mirror stops.
- `rex app list`, `rex app open Spotify`, `rex app close`, `rex app info` and `rex app favourite`
  work with or without the desktop app.

### Changed

- The side panel's tabs are Controls, Apps, Phone, Settings and Info, and Ctrl+Alt+1 to 5 follow
  that order: Phone is now Ctrl+Alt+3, Settings Ctrl+Alt+4 and Info Ctrl+Alt+5.
- Only one scrcpy server starts at a time, whatever starts it: the mirror, a copy or reading the
  app list never race each other on the phone.

## 2.5.0 - 2026-10-01

### Added

- The phone's sound on this PC has a level of its own: the sound button in the top bar opens a
  panel with a volume slider, mute and a level meter, and the phone's own volume is never touched.
  Each phone remembers its level.
- Ctrl+Alt+PageUp and Ctrl+Alt+PageDown change it, Ctrl+Alt+Shift+M mutes it, the wheel over the
  button works too, and the three can be keys from anywhere.
- Settings has a **Sound on this PC** group: start muted, mute while the window is hidden or behind
  others or while this PC is locked, lower it while you type, fades, balance, and following the
  Windows volume mixer.
- When there is no sound to control, the panel says why: phone sound off, an Android older than
  11, or no mirror yet.
- `rex sound 40`, `rex sound mute` and `rex sound` set and show it from the command line.

## 2.4.0 - 2026-10-01

### Added

- Keys that work from anywhere. Ctrl+Alt+M brings the mirror up from any app, even from the tray,
  and sends it away again; what it does when the window is behind others or in front is yours to
  choose, and it can open straight to fullscreen.
- Any action can have a key of its own that works while the window is hidden, such as the next
  video or a screenshot, with an optional word from the tray to say it worked.
- Settings has a **Shortcuts from anywhere** group: click a box and press the keys. A key that
  Windows or the window already uses is refused with the reason, and the box warns when your
  keyboard layout types a character with the key or another app has claimed it.
- The tray menu shows the key beside **Open**, and has a switch for keys from anywhere. The Info
  tab lists your keys from anywhere.

### Fixed

- Ctrl+Alt+Shift+1 to 4 no longer switch tabs, and Ctrl+Alt+F1 no longer starts the tour: each
  key of the window now needs exactly its own modifiers.

## 2.3.4 - 2026-10-01

### Fixed

- Quitting the app while the soft background or the navigator picture was being drawn could crash
  it on the way out. Closing now waits for the picture in progress before letting go of it.
- A touch that Windows refused to lift could leave every later touch refused as well, so taps and
  swipes from the touchpad or the keyboard could stop reaching the phone. A refused touch is now
  let go of and the next one starts cleanly.
- Saving a setting while another program was reading config.json could close the app. The app now
  waits a moment for the other program, and if it still cannot save, says so in the status bar.

## 2.3.3 - 2026-10-01

### Added

- The website is now nine pages: features, a guide, every keyboard shortcut, every setting, every
  command, help by symptom and this list of changes. The shortcut, settings and command pages are
  made from the app itself, so they always match it.
- The Info tab has a **Help** section: the guide, every setting explained, the shortcuts, what is new
  in this version, and a way to report a problem with the version already filled in.
- After an update, the bar above the mirror says once what version you are on, with a link to what
  changed. **Say what is new after an update** in Settings turns it off.
- `rex help` points at the command line page, and lists exactly the commands `rex` accepts.

### Changed

- The site is checked before it publishes: every link, picture, title and heading.

### Fixed

- Changing the recording format while every session is recorded now offers to restart the mirror,
  as the other settings that apply at the next start do.

## 2.3.2 - 2026-10-01

### Fixed

- `rex action keyboard-layout` opens the phone's keyboard settings, as the button in the window does.
- A command whose output arrives late on a busy PC is no longer read as empty.

### Changed

- Settings remember which groups you left open.
- **Find a setting** also finds a setting by its name in config.json, such as `Mirror.MaxFps`.
- **Reset every app setting** resets every setting, including any added later.
- The log names each step of starting up, and a failure is written with its real cause in full,
  even when logging is off. `rex diagnostics` adds the version, Windows and .NET, the program
  Windows starts at sign-in (and a warning when it is not this one), whether config.json and
  state.json can be read, and whether scrcpy and adb run.
- An unexpected problem while the window is open is reported once and the app carries on.

## 2.3.1 - 2026-10-01

### Changed

- Copies of the phone that cannot be seen (window hidden or minimised, phone on its side, window
  too narrow) are paused after five seconds and start again as soon as there is room. Two copies in
  the tray went from about 22 % of the PC to under 8 %.

## 2.3.0 - 2026-10-01

### Changed

- The Settings tab is finished: the restart offer stays in sight above every tab, rows line up,
  and **Find a setting** filters row by row.
- Settings whose parent is off are greyed out with the reason, instead of doing nothing.

## 2.2.1 - 2026-10-01

### Changed

- The Phone tab's **Reset** only appears on a setting that can be reset and has been changed,
  slider values sit on their label's line, and keyboard focus stays where it was after a change.
- The Info tab lists every shortcut in one column with plain names.

### Fixed

- Holding left Alt for the PC view can no longer get stuck if Windows misses the key coming up.
- A Phone tab change made while another was still being read is no longer lost.

## 2.2.0 - not published

This version was built but never released; its changes shipped in 2.2.1.

### Added

- The window can stay on top, the side panel can sit on the left, and you choose which phone
  buttons the top bar shows.
- The status bar can show the frame rate and say when a phone connects or leaves.
- Settings for the keyboard swipes: how far and how fast they go.

## 2.1.0 - 2026-09-30

### Added

- Settings for the video (buffer, renderer, trying a lower resolution), the audio (what to
  capture, its quality, keeping it playing on the phone), the session (the phone's screen timeout
  while mirrored, keeping this PC awake, an app to open), every mouse button, key repeat, hover,
  the clipboard, game controllers and the recording format.

## 2.0.9 - 2026-09-30

### Fixed

- The picture keeps the video's shape from the moment it appears, instead of briefly taking the
  window's.

## 2.0.8 - 2026-09-30

### Fixed

- Copies of the phone pause, turn and flip together with the main view.

## 2.0.7 - 2026-09-30

### Added

- Copies of the phone, side by side: extra live views you can touch and type into.
- **Repair USB driver** fixes "USB device not recognised" from the app, and can do it by itself.
- The Phone tab shows dark mode, display size and brightness in words.

## 2.0.6 - not published

This version was built but never released; its changes shipped in 2.0.7.

### Changed

- The soft background and the navigator read the picture on the graphics card, which costs far
  less, and every control is drawn in the app's own style.

## 2.0.5 - 2026-09-30

### Added

- Every key types on the phone, including digits, symbols and AltGr.
- Browse mode (**Ctrl+Alt+K**): the arrow keys, Enter, L and M move through a feed.
- The window teaches itself with a short tour, and every control has a name a screen reader reads.
- The fullscreen controls can be dragged anywhere.

### Changed

- The soft background costs much less, and the pattern guide follows where Android puts the dots.

## 2.0.4 - 2026-09-23

### Fixed

- Tests and the release pipeline are reliable again; no change to the app itself.

## 2.0.3 - 2026-09-23

### Fixed

- Steadier fullscreen and pattern-guide tests; no change to the app itself.

## 2.0.2 - 2026-09-23

### Fixed

- Updating stops the app's own processes first, so the installer never finds its files in use.

## 2.0.1 - 2026-09-23

### Fixed

- Mirroring works again after the first installer release, and the pattern guide is clearer.

### Changed

- A new version is published automatically once it passes every test.

## 2.0.0 - 2026-09-23

### Added

- The first installer: per-user, no administrator prompt, with scrcpy included.
- A guided first run inside the window that shows the exact taps to allow USB debugging.
- The command line, `rex`, for people and for scripts.
