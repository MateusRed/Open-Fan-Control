# Builds a self-contained Open Fan Control (bundles the .NET runtime, so no install
# is required) and launches it elevated. Fan control needs administrator rights.
# Usage:  powershell -ExecutionPolicy Bypass -File .\run-admin.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\OpenFanControl\OpenFanControl.csproj'
$exe  = Join-Path $root 'publish\OpenFanControl.exe'

# Locate dotnet (global install or the per-user install used during setup).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet SDK not found. Install the .NET 8 SDK first.' }

if (-not (Test-Path $exe)) {
    Write-Host 'Publishing self-contained build (first run only)...' -ForegroundColor Cyan
    & $dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $root 'publish')
}

Write-Host 'Launching elevated (accept the UAC prompt)...' -ForegroundColor Cyan
Start-Process -FilePath $exe -Verb RunAs
