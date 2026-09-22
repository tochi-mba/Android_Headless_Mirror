@echo off
setlocal
cd /d "%~dp0"

rem Developer launcher for this checkout. Everyone else installs the app from the website.
rem   REX.bat            builds tools\rex on first use, then opens Android Headless Mirror
rem   REX.bat <command>  runs the command line (REX.bat help)
rem   REX.bat --build    rebuilds tools\rex from the current source, then opens the app

set "OUT=%~dp0tools\rex"

if /I "%~1"=="--build" goto build
if not exist "%OUT%\RexMirror.exe" goto build
if not exist "%OUT%\rex.exe" goto build
goto run

:build
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Building from source needs the .NET 10 SDK: https://dot.net 1>&2
  exit /b 1
)
echo Building Android Headless Mirror from source... 1>&2
dotnet publish "%~dp0src\Rex.Mirror\Rex.Mirror.csproj" -c Release -r win-x64 --self-contained true -o "%OUT%" -nologo -v quiet 1>&2
if errorlevel 1 exit /b 1
dotnet publish "%~dp0src\Rex.Cli\Rex.Cli.csproj" -c Release -r win-x64 --self-contained true -o "%OUT%" -nologo -v quiet 1>&2
if errorlevel 1 exit /b 1
if /I "%~1"=="--build" (
  start "" "%OUT%\RexMirror.exe"
  exit /b 0
)

:run
if "%~1"=="" (
  start "" "%OUT%\RexMirror.exe"
  exit /b 0
)
"%OUT%\rex.exe" %*
