@echo off
rem Opens the Wireless ECR Simulator. Compiles in memory (no exe), like the ECR Simulator.
rem Wireless ECR Simulator - by Ahmad El Lahib
start "" powershell -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0Start-WirelessEcrSimulator.ps1"
