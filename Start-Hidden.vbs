Option Explicit
Dim shell, fso, base, cmd
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & base & "\Start-PhoneMirror.ps1"""
shell.Run cmd, 0, False
