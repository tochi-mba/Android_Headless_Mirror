# Changelog

What changed in each version of Android Headless Mirror, newest first. Every version listed here
was built and tested by CI; the installer for each published one is on the
[releases page](https://github.com/tochi-mba/Android_Headless_Mirror/releases).

## 2.16.1 - 2026-10-06

### Fixed

- The phone sound panel opens inside the window, under its button, instead of hanging past the
  window's right edge.
- The website shows real pictures of the app, and describes every feature it has now.

## 2.16.0 - 2026-10-06

### Added

- **Controls tab**, a group in Settings: show or hide each section of the Controls tab and move
  it up or down; take tiles out of the Phone, View and gesture grids, move them, or add any
  action as a tile; two, three or four tiles to a row, with or without their names.
  **Put the Controls tab back** returns it as it ships.
- The floating controls can **keep the order you pick them in**.
- Every action has a picture of its own, so any of them reads well as a tile.

### Fixed

- The Controls tab's keyboard line names browse mode's keys as you have them.

## 2.15.0 - 2026-10-06

### Added

- **Keyboard shortcuts**, a group in Settings: every key the window answers to, and every action
  that has none yet, in a box of its own. Click it and press the keys; Backspace takes a key away
  and **Put back** returns the one it shipped with. A key another action has, one Windows keeps
  for itself, or one that would fire while typing into the phone is refused in words.
- Browse mode's keys can be your own too: any single key for any action. Space still taps unless
  it is given something else.
- `rex keys` lists every key; `rex keys set <action> <key>`, `rex keys set <action> none` and
  `rex keys reset [action]` change them, with `--browse` for browse mode. A running app follows.
- Tooltips, the Info tab, the fullscreen controls and the browse mode line say your own keys.

## 2.14.0 - 2026-10-06

### Added

- **Around the picture**, in Window: the app's own dark, or **black** for an OLED screen. The
  mirror area turns at once; the edges scrcpy draws itself follow at the next start.
- The floating controls can show **in the window too**, over the top of the mirror, and can be
  kept on screen instead of **hiding by themselves**.
- **Check for a newer version once a day**, in Startup & tray: off unless you turn it on. It asks
  GitHub which version is newest and nothing else; a newer one is offered in the bar above the
  mirror with **Download**, **What's new**, **Not now** and **Skip this version**.

## 2.13.0 - 2026-10-05

### Added

- **Window**, a group of its own in Settings: the **top bar** and the **status bar** can each be
  hidden, the **side panel's size** goes from 80% to 150%, the window can **open where it was**
  or in the middle of the screen, **fit itself to the phone** as each mirror starts, and **ask
  before quitting** while a phone is mirrored. Startup & window is now Startup & tray.
- **Fit the window to the phone** (Ctrl+Alt+F, and an action for the fullscreen controls and the
  command line): the window takes the shape of the phone, its copies or both phones.
- **Name screenshots and recordings** with {date}, {time}, {phone}, {model} and {n}; **flash the
  view** as a screenshot is taken, and **open each screenshot** once it is saved.
- How far the **zoom keys and buttons** step, whether the **soft background stays while zoomed
  in**, the lock screen guide's **dot size**, a word from the tray when **the mirror stops by
  itself**, and **writing every phone command in the log** for a bug report (where the screen
  was touched is left out).

### Fixed

- Two screenshots taken in the same second no longer overwrite each other, in the app and from
  the command line.
- A screenshot taken over the pipe now flashes and opens like one taken in the window.

## 2.12.0 - 2026-10-05

### Added

- **More of scrcpy in Settings**, each applying the next time the mirror starts. In Picture: the
  phone's **video encoder** (read from the phone with **Read the phone's encoders**), **show only
  part of the screen**, **when the phone turns** (keep the picture upright, on its side, or as it
  started), **start the view turned**, **tilt the picture**, **smooth the picture when it is shown
  smaller**, and **show taps on the phone**. In Audio: a **playback buffer** for sound that
  crackles, and **only mirror with sound**. In Phone screen & session: **stop the mirror after** a
  set time, and how many **times in a row** a mirror that closes by itself is started again.
- **Copy frame rate** in Copies: lighter copies at 15 to 60 fps, while the mirror keeps its own.
- `rex encoders` lists the phone's video encoders, with or without the app running.
- A mirror stopped by its time limit says so and stays stopped until you press Start; an encoder
  the phone does not have is named in words instead of leaving the mirror waiting.

## 2.11.0 - 2026-10-05

### Added

- **Every setting has a control.** The last 21 values that only `config.json` held are in the
  Settings tab: the sound's format and buffer, where recordings go, keeping the phone from sleeping,
  waking it and dismissing a lock screen with no PIN, touchpad gestures, Alt + wheel and Alt + pinch
  zoom on their own, four more for the lock screen guide, and a new **Connection & logs** group for
  USB first, how often phones are looked for, the wait before trying again, the Wi-Fi debugging
  port, phones to try by address, and the log.
- **Changed**, beside the search box, lists every setting that is not as the app ships, with its
  value now and **Put back** for each, or **Put them all back**. Each group ends with **Put this
  group back to how it ships**. Only what you changed moves.
- An address typed in for a phone on Wi-Fi must be on this PC's own network; anything else is
  refused in words.
- The Display group is called Picture.

### Fixed

- The Phone tab could show the settings of the phone it was reading before you switched phones.

## 2.10.0 - 2026-10-05

### Added

- **Two phones side by side.** When a second, different phone connects, the notice bar asks whether
  to show it beside the first (or it is shown at once, or only from the phone menu, as set). Each
  phone gets a view of its own in its own shape, side by side or one above the other, and a session
  of its own: its own screen-off, staying awake, resolution and bit rate, and its own restarts.
- The side panel, the keys and the buttons act on the phone you clicked; a strip above the tabs
  says which, with a button for the other. **Ctrl+Alt+O** switches, and screenshots, gestures,
  the Phone tab, Apps, Info and the sound button all follow.
- From the phone menu: show a ready phone beside, use either phone, make the one beside the main
  phone, or stop showing it. The phone shown beside comes back by itself when it connects again.
- A phone on USB and on Wi-Fi at once is one phone, never shown beside itself. Two phones of the
  same model are told apart by the end of their serials.
- Sound on this PC from the main phone, from the phone you are using (the other is muted here), or
  from both.
- `rex phones` lists every phone and where it shows, and shows one beside, stops it, switches or
  swaps them while the app runs.
- A **Second phone** group in Settings for all of it, including pausing it while the window is
  hidden and applying its own profile while you use it.

## 2.9.0 - 2026-10-04

### Added

- **Profiles**: named lists of settings to switch to in one go, one for games, one for films, one
  for battery. Applying one changes only the settings in it; the rest stay as you have them.
- Save the settings you have now as a profile from **Settings → Profiles**, with only what differs
  from how the app ships or every setting, and only the groups you choose. Update, rename,
  duplicate, reorder, export and import them; each says how many of its settings have changed since.
- Six presets to apply as they are or start from: Lowest latency, Best picture, Battery saver,
  Presentation, Gaming and Quiet. Each lists what it changes before you apply it.
- Profiles that switch by themselves: one for fullscreen, one for when this PC runs on battery, and
  each phone's own when it connects. When one ends it puts back exactly what it changed, unless you
  changed that setting meanwhile.
- Ctrl+Alt+F1 to F9 apply the first nine profiles, the tray menu has a Profiles submenu, and
  `rex profile list|show|apply|save|rename|delete|export|import` works with or without the app.
- A profile that changes how the mirror starts offers a restart, or restarts the mirror at once, as
  you choose.
- **What's new, only when there is something new.** After an update that brought features you did
  not have, the window shows just those, once, with **Show me around** pointing at each one where it
  lives. An update with only fixes says which version this is and nothing more.

## 2.8.0 - 2026-10-03

### Added

- A **second screen** for one app: the app runs on a display of its own on the phone, shown in the
  window beside the phone or instead of it, while the phone shows something else. By default it is
  exactly the size of its place in the window, so nothing is letterboxed and text stays sharp;
  1280 x 720, 1920 x 1080, 2560 x 1440, the phone's own size or your own are there too.
- Open it from **Controls → Second screen**, the Apps tab (an app's menu, or "Clicking an app opens
  it" set to a second screen or to ask), Ctrl+Alt+D, or `rex screen open YouTube`.
- Home, Back, Recents and the notification shade act on the view that has the keyboard: on the
  second screen they go to its display. A name on each view and an outline round the one in use
  show which is which; a splitter between them sets how the space is shared.
- Closing it moves its app back to the phone (or closes it, as set), and it can come back with its
  app whenever the mirror starts. Copies wait while it is open, and come back after.
- Settings has a **Second screen & views** group: where it goes, its size, upright or on its side,
  text size, resolution, Android's navigation bar, where the on-screen keyboard appears, and how
  views are arranged and marked.

### Fixed

- A question asked in the window, such as whether to install an app or uninstall one, is no longer
  cut in half by the phone's picture: the picture steps aside while it asks.
- A change made to `config.json` outside the app is picked up even while the PC is busy.

## 2.7.0 - 2026-10-02

### Added

- Drop files or folders onto the window, choose them from Controls, paste files copied in File
  Explorer with Ctrl+Alt+V, or add **Send to › Android phone** to Explorer. Each transfer stays in
  the Controls tab with progress, cancel, retry and a clear result.
- APK files install after asking. Updates, downgrades, test builds, granted permissions and opening
  the app afterwards are all separate settings.
- File-name clashes can ask, keep both with a numbered name, replace or skip. Photos, videos and
  music can go to their own shared-storage folders, and new media can appear in Gallery at once.
- `rex push <files…>` and `rex install <apks…>` work without the desktop app and support the JSON
  agent mode.

### Changed

- scrcpy's own drops onto the phone picture appear in the same transfer history. Turning file drops
  off turns that path off too, after a restart; a new destination applies at once, with no restart.
- Several files may be sent together, from one to four at once. A disconnect can cancel the queue,
  partial files are cleaned up, and hidden-window notifications and taskbar progress are optional.
- For people starting out, the setup guide and the tour now show the Apps tab and the ways to send
  files.

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
