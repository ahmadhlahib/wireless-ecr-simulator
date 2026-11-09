@echo off
rem Builds "Wireless ECR Simulator.exe" with the .NET Framework 4 compiler that ships with Windows (no SDK needed).
rem Wireless ECR Simulator - by Ahmad El Lahib
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
if not exist "%WPF%" set WPF=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\WPF
if not exist "%~dp0assets\app.ico" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-icon.ps1"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0Wireless ECR Simulator.exe" ^
    /win32icon:"%~dp0assets\app.ico" ^
    /resource:"%~dp0assets\app.ico",WirelessEcrSim.app.ico ^
    /resource:"%~dp0src\MainWindow.xaml",WirelessEcrSim.MainWindow.xaml ^
    /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
    /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Core.dll ^
    "%~dp0src\*.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built "%~dp0Wireless ECR Simulator.exe"
