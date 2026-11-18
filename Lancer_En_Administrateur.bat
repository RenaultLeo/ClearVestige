@echo off
cd /d "%~dp0"
if not exist "ClearVestige.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "build.ps1"
)
powershell.exe -NoProfile -Command "Start-Process -FilePath '%~dp0ClearVestige.exe' -Verb RunAs"
