@echo off
rem Wireless ECR Simulator (by Ahmad El Lahib). Starts the .exe, or the no-exe version when the .exe is missing
rem (some antivirus tools remove or block unsigned .exe files).
if exist "%~dp0Wireless ECR Simulator.exe" (
    start "" "%~dp0Wireless ECR Simulator.exe" %*
) else (
    start "" powershell -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0Start-WirelessEcrSimulator.ps1" %*
)
