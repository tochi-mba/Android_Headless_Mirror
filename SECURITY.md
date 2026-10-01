# Security

## Reporting a problem

Please report a security problem privately, not in a public issue. Use the repository's
**Security** tab → *Report a vulnerability* if it is offered; otherwise open an issue that says
only that you have a security problem to report, without any details, and ask for a private way
to send them. Say which version you have (Info tab, or `rex diagnostics`), what you did, and what
happened. A fixed release goes out as soon as the fix has passed every test.

## What the app may do

- **Control the phone over ADB**, which you allow on the phone once with *Allow USB debugging*.
  USB debugging gives a PC full control of a phone, so only allow computers you trust.
- **Run scrcpy**, the open-source mirroring engine, downloaded from its official releases and
  checked against the published SHA-256 before it is used.
- **Repair the phone's USB connection** with administrator approval, always through the Windows
  administrator prompt. The optional automatic repair is a Windows task that runs only Windows' own
  `pnputil.exe` from System32, with fixed arguments that touch only devices Windows has already
  failed to read. It never runs anything from the app's own folder, which your user account can
  write to.
- **Connect to GitHub** to download the app and scrcpy, and to open the website when you ask.

## What it never does

- Store, type or send a PIN, password or pattern. The pattern guide only draws where the dots are.
- Upload the mirrored screen, what you type, lock information or anything on the phone. There is
  no account and no telemetry.
- Root, flash or otherwise change the phone beyond the settings you change yourself.
- Stop the shared ADB server (`adb kill-server`), which other programs may be using.
- Turn on wireless ADB unless you switch it on.
- Run its own programs with administrator rights without the Windows prompt.
