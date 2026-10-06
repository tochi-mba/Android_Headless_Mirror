# Contributing

Thank you for helping. This file is how to build, test and ship a change, and the checklists every
change goes through. [AGENTS.md](AGENTS.md) holds the architecture and the invariants in full.

## Build and run

```powershell
REX.bat                     # builds tools\rex from this checkout on first use, then opens the app
REX.bat --build             # rebuilds after a change
dotnet build Rex.sln -c Release
```

You need the .NET 10 SDK on Windows. The installer needs Inno Setup 6:
`./installer/build.ps1 -Version 0.0.0` writes `dist/AndroidHeadlessMirror-Setup.exe`.

## Test

```powershell
# the fast tests: everything without a desktop window
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj -c Release --no-build -- --filter-not-class Rex.Tests.AppEndToEndTests Rex.Tests.AppUiTests

# the website
npm ci; npm run check:site; npm run test:pages
```

The tests come in layers:

| Layer | Where | What it proves |
|-------|-------|----------------|
| Unit | `tests/Rex.Tests/*Tests.cs` | Rex.Core's rules, one test per branch |
| WPF | `Wpf.Run(...)` in the same files | panels and their rules without a desktop window |
| End to end | `AppEndToEndTests.*` | the real app against `tests/Rex.FakeAdb` and `tests/Rex.FakeScrcpy`, with real input |
| UI automation | `AppUiTests.*` | every control, through UI Automation (`x:Name` is the AutomationId) |
| CLI | `CliTests` | `rex` in both modes |
| Repository | `RepositoryTests`, `SiteTests` | the rules below, and that the site matches the app |
| Website | `tests/pages.spec.js` | every page loads, passes axe, and never scrolls sideways |

The desktop suites (end to end and UI automation) put a window on screen; CI runs them on every
pull request. Tests wait for something observable (a status field, a log line), never for time.
The fakes stand in for the PC: tests never reach the real USB devices, Task Scheduler, the
administrator prompt or the browser.

Every Rex.Core file listed in `tests/coverage-required.txt` must stay 100 % line-covered; CI fails
otherwise. Add each new Core file to that list.

### What the app costs

`CostCheck` measures the app on CI's runner with the fake phone: how long it takes to start
mirroring, then its CPU (percent of one core), memory, handles and threads while mirroring, zoomed
in, with Settings open, hidden in the tray and shown again. The numbers are compared with
`tests/perf-baseline.json`; a run fails when one rises above *baseline x factor + allowance* (the
tolerance is in the same file). The job's summary shows the table, and the `desktop-ui-perf`
artefact holds the run's own `perf-baseline.json`. To move the baseline after a change that makes
the app cheaper, or one that is meant to cost more, copy that file over the committed one in the
same pull request. Locally it runs with `REX_PERF=1`, but its numbers are your PC's, not the runner's.

## The site and the docs

The website in `docs/` is plain HTML with no build step. Four pages are made from the app itself:
`settings.html`, `shortcuts.html`, `cli.html` and `changelog.html`. When you change a setting, a
shortcut, a command or `CHANGELOG.md`, run the site tests once with `REX_WRITE_SITE=1` and commit
the pages they rewrite:

```powershell
$env:REX_WRITE_SITE = "1"; dotnet test --project tests/Rex.Tests/Rex.Tests.csproj -c Release --no-build -- --filter-class Rex.Tests.SiteTests; Remove-Item Env:REX_WRITE_SITE
```

Every change people will notice adds its entry to `CHANGELOG.md`, newest first, in plain words.

## Checklists

**Adding a setting**

1. A property with a default in its settings record, and a clamp in `Normalize`.
2. A line in the committed `config.json`, in declaration order.
3. A control in the Settings tab with an `x:Name`, an accessible name and a hint where the label is
   not enough, disabled while its parent setting is off.
4. An entry in `SettingsCatalogue`; if it is passed to scrcpy, in `SettingsCatalogue.NextStart` too.
5. A unit test of its clamp and its effect, and a UI or end-to-end test that it saves and applies.
6. The settings page rewritten (above) and a `CHANGELOG.md` entry.

**Adding a shortcut**

1. An entry in `Shortcuts.All`; the window, the Info tab and the website all read it.
2. Never AltGr, and never a key that types.
3. A test that it does what it says, and the shortcuts page rewritten.

**Adding a page to the site**

1. Copy the header and footer from `docs/index.html` exactly; mark the page in the header with
   `aria-current="page"`.
2. One `h1`, a description, a canonical address, and a line in `docs/sitemap.xml`.
3. `npm run check:site` and `npm run test:pages` pass, and the page is added to the lists at the
   top of `tests/pages.spec.js`.

**Every change**

- Words: plain, sentence case, saying what happens rather than how. Errors say what to do next.
- No file over 1,000 lines, no dead code, warnings are errors.
- No new scripts beyond `REX.bat`, `assets/make-icon.ps1`, `installer/*.ps1` and the Node
  scripts in `scripts/`.
- Every new control has an `x:Name`, an accessible name and a UI test.

## Shipping

One pull request per change, on a branch from an up-to-date `main`. Its last commit sets the new
version in `Directory.Build.props` ("Version X.Y.Z so the release publishes"). CI runs five jobs
(build and fast tests, desktop end to end, desktop UI automation, the website, and on `main` the
release); a red job is fixed, never retried blindly. Pull requests are merged with a rebase, and
a successful `main` run publishes the installer for any version that has no release yet.
