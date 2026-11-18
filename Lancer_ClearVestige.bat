@echo off
cd /d "%~dp0"
if not exist "ClearVestige.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "build.ps1"
)
start "" "ClearVestige.exe"
