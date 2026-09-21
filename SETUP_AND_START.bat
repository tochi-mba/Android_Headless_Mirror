@echo off
setlocal
cd /d "%~dp0"
echo.
echo === Android Headless Mirror - setup ===
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
if errorlevel 1 (
  echo.
  echo Setup failed. See the error above.
  pause
  exit /b 1
)
echo.
echo Setup finished. Starting the mirror supervisor...
if exist "%~dp0stop.flag" del /q "%~dp0stop.flag"
wscript.exe "%~dp0Start-Hidden.vbs"
echo.
echo The supervisor is now running in the background.
echo Connect an Android device by USB. On the FIRST connection only,
echo accept "Allow USB debugging" and tick "Always allow from this computer".
echo.
pause
