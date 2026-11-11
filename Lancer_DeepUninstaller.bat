@echo off
cd /d "%~dp0"
if not exist "DeepUninstaller.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "build.ps1"
)
start "" "DeepUninstaller.exe"
