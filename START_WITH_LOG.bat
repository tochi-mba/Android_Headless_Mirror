@echo off
cd /d "%~dp0"
if exist "%~dp0stop.flag" del /q "%~dp0stop.flag"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-PhoneMirror.ps1" -Foreground
pause
