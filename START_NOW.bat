@echo off
cd /d "%~dp0"
if exist "%~dp0stop.flag" del /q "%~dp0stop.flag"
wscript.exe "%~dp0Start-Hidden.vbs"
