@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap-RexCli.ps1"
if errorlevel 1 (
  echo {"ok":false,"protocolVersion":1,"command":"bootstrap","error":{"type":"BootstrapError","message":"REX CLI bootstrap failed. See stderr/stdout above."}}
  exit /b 1
)

"%~dp0tools\rex\rex.exe" --json %*
