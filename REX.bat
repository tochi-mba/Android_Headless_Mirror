@echo off
setlocal
cd /d "%~dp0"

set "REX_BOOTSTRAP_QUIET="
if /I "%~1"=="--json" set "REX_BOOTSTRAP_QUIET=-Quiet"
if /I "%~1"=="--plain" set "REX_BOOTSTRAP_QUIET=-Quiet"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap-RexCli.ps1" %REX_BOOTSTRAP_QUIET%
if errorlevel 1 (
  if not defined REX_BOOTSTRAP_QUIET (
    echo.
    echo REX CLI bootstrap failed. See the error above.
  )
  exit /b 1
)

"%~dp0tools\rex\rex.exe" %*
