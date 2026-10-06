@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0bin\DanMuLAN.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)
start "" "%~dp0bin\DanMuLAN.exe"
