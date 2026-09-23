; Android Headless Mirror — Windows installer (Inno Setup 6).
; Built by installer\build.ps1: ISCC /DAppVersion=<x.y.z> /DSource=<dist folder> AndroidHeadlessMirror.iss
;
; Per-user install (no administrator prompt) into %LocalAppData%\Programs. The app keeps its own
; data in %LocalAppData%\REX\Android Headless Mirror; the uninstaller keeps it unless asked.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Source
  #define Source "..\dist"
#endif

#define AppName "Android Headless Mirror"
#define AppExe "RexMirror.exe"
#define CliExe "rex.exe"
#define RunValue "AndroidHeadlessMirror"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{6C1E7A2B-5D3F-4E8A-9B0C-7A2D4F6E8B10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=REX Technologies
AppPublisherURL=https://tochi-mba.github.io/Android_Headless_Mirror/
AppSupportURL=https://github.com/tochi-mba/Android_Headless_Mirror/issues
AppUpdatesURL=https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest
AppComments=Your Android phone in one window on this PC, over USB.
AppContact=https://github.com/tochi-mba/Android_Headless_Mirror/issues
VersionInfoDescription={#AppName} setup
VersionInfoProductName={#AppName}
VersionInfoCompany=REX Technologies
; A second installer would fight the first over the same files.
SetupMutex=AndroidHeadlessMirrorSetup
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputBaseFilename=AndroidHeadlessMirror-Setup
SetupIconFile=..\assets\rex.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; The wizard is three clicks, so the words on it are most of what anyone reads about the app.
[Messages]
SelectTasksLabel2=Choose how {#AppName} should behave, then click Next.%n%nIt installs for you only, without administrator rights, and puts the rex command on your PATH.
FinishedHeadingLabel=Ready to mirror
FinishedLabelNoIcons=Plug your Android phone in with USB debugging turned on and tap Allow when it asks. The mirror opens by itself, and the app will show you the exact taps if you have not turned it on yet.
FinishedLabel=Plug your Android phone in with USB debugging turned on and tap Allow when it asks. The mirror opens by itself, and the app will show you the exact taps if you have not turned it on yet.
ConfirmUninstall=Remove {#AppName} from this PC?%n%nYour phone is not touched, and nothing on it changes.

[Tasks]
Name: "autostart"; Description: "Start with Windows and wait for the phone in the tray"; GroupDescription: "After installing:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "After installing:"; Flags: checkedonce

[Files]
Source: "prepare-upgrade.ps1"; Flags: dontcopy
Source: "{#Source}\rex\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Source}\scrcpy\*"; DestDir: "{app}\scrcpy"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value the app's "Start with Windows" setting manages, so both stay in sync.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: autostart
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "{#RunValue}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent
Filename: "https://tochi-mba.github.io/Android_Headless_Mirror/#install"; Description: "Show me how to turn on USB debugging"; Flags: shellexec nowait postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{app}\{#CliExe}"; Parameters: "quit"; Flags: runhidden; RunOnceId: "QuitApp"

[Code]
const
  EnvironmentKey = 'Environment';

// The app hides to the tray on WM_CLOSE, so ask it to quit properly before files are replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Arguments: String;
  ResultCode: Integer;
begin
  Result := '';
  ExtractTemporaryFile('prepare-upgrade.ps1');
  Arguments := ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{tmp}\prepare-upgrade.ps1" -InstallDirectory "{app}"');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := 'Could not start upgrade preparation. Close REX and retry.';
  if (Result = '') and (ResultCode <> 0) then
    Result := 'Could not stop the installed REX or Android tools. Close them and retry. No files have been replaced.';
end;

// Put the install folder on the user's PATH so "rex" works in any terminal.
procedure EnvAddPath(Path: String);
var
  Paths: String;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;
  if (Paths <> '') and (Paths[Length(Paths)] <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Path);
end;

procedure EnvRemovePath(Path: String);
var
  Paths: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    exit;
  P := Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';');
  if P = 0 then
    exit;
  Delete(Paths, P - 1, Length(Path) + 1);
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths);
end;

// On an upgrade, mirror what the app currently has: the user may have changed it in Settings.
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and FileExists(ExpandConstant('{app}\{#CliExe}')) then
  begin
    if RegValueExists(HKCU, '{#RunKey}', '{#RunValue}') then
      WizardSelectTasks('autostart')
    else
      WizardSelectTasks('!autostart');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnvAddPath(ExpandConstant('{app}'));
end;

// Settings, screenshots and logs outlive the app on purpose, so removing them is asked for
// rather than assumed, and never happens during a silent uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDirectory: String;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;

  EnvRemovePath(ExpandConstant('{app}'));
  DataDirectory := ExpandConstant('{localappdata}\REX\{#AppName}');
  if not DirExists(DataDirectory) then
    exit;

  if UninstallSilent then
    exit;

  if MsgBox('Also delete your settings, screenshots and logs?' + #13#10 + #13#10 +
      DataDirectory + #13#10 + #13#10 +
      'Keep them if you plan to install {#AppName} again.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDirectory, True, True, True);
end;
