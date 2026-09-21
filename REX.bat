@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap-RexCli.ps1"
if errorlevel 1 (
  echo.
  echo REX CLI bootstrap failed. See the error above.
  exit /b 1
)

"%~dp0tools\rex\rex.exe" %*
