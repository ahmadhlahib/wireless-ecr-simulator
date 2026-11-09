# Starts the Wireless ECR Simulator without an .exe: compiles src\*.cs in memory and opens the window,
# the same way as the ECR Simulator (Start-EcrSimulator.ps1). Wireless ECR Simulator - by Ahmad El Lahib
# Usage: powershell -STA -ExecutionPolicy Bypass -File Start-WirelessEcrSimulator.ps1 [-Snapshot file.png]
param([string]$Snapshot, [switch]$Demo)

$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Web.Extensions, System.Security, System.Net.Http
if (-not (Test-Path (Join-Path $dir 'assets\app.ico'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'tools\make-icon.ps1') | Out-Null
}

[string[]]$sources = Get-ChildItem (Join-Path $dir 'src') -Filter *.cs | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
$refs = 'System', 'PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml', 'System.Web.Extensions', 'System.Security',
        'System.Net.Http', 'System.Core', 'System.Xml' |
    ForEach-Object { [Reflection.Assembly]::LoadWithPartialName($_).Location }

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$options = New-Object System.CodeDom.Compiler.CompilerParameters
$options.GenerateInMemory = $true
$options.GenerateExecutable = $false
$options.ReferencedAssemblies.AddRange([string[]]$refs)
$result = $provider.CompileAssemblyFromSource($options, $sources)
$errors = @($result.Errors | Where-Object { -not $_.IsWarning })
if ($errors.Count -gt 0) {
    $text = ($errors | Select-Object -First 10 | ForEach-Object { "line $($_.Line): $($_.ErrorText)" }) -join "`n"
    [Windows.MessageBox]::Show("The simulator did not compile:`n`n$text", 'Wireless ECR Simulator') | Out-Null
    throw $text
}

$argList = @()
if ($Snapshot) { $argList += @('--snapshot', $Snapshot) }
if ($Demo) { $argList += '--demo' }
$program = $result.CompiledAssembly.GetType('WirelessEcrSim.Program')
[void]$program.GetMethod('Run').Invoke($null, @(($dir + '\'), [string[]]$argList))
