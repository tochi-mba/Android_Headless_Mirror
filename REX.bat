@echo off
setlocal
cd /d "%~dp0"

rem REX.bat            opens Android Headless Mirror
rem REX.bat <command>  runs the command line (rex help)
rem REX.bat --source   builds this checkout and opens it

if /I "%~1"=="--source" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap-Rex.ps1" -Source -Force
  if errorlevel 1 (
    echo REX could not be built from source. See the messages above.
    exit /b 1
  )
  start "" "%~dp0tools\rex\RexMirror.exe"
  exit /b 0
)

set "REX_QUIET="
if /I "%~1"=="agent" set "REX_QUIET=-Quiet"
for %%A in (%*) do (
  if /I "%%~A"=="--json" set "REX_QUIET=-Quiet"
  if /I "%%~A"=="--plain" set "REX_QUIET=-Quiet"
)

if not exist "%~dp0tools\rex\rex.exe" goto prepare
if not exist "%~dp0tools\rex\RexMirror.exe" goto prepare
goto ready

:prepare
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap-Rex.ps1" %REX_QUIET%
if errorlevel 1 (
  if not defined REX_QUIET echo REX could not be prepared. See the messages above.
  exit /b 1
)

:ready
if "%~1"=="" (
  start "" "%~dp0tools\rex\RexMirror.exe"
  exit /b 0
)

"%~dp0tools\rex\rex.exe" %*
