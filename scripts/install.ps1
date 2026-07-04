# Open Fan Control — quick installer.
#
#   irm https://raw.githubusercontent.com/MateusRed/Open-Fan-Control/main/scripts/install.ps1 | iex
#
# Downloads the latest self-contained build into %LocalAppData%\OpenFanControl and adds
# Start-Menu & Desktop shortcuts. No .NET install required. Fan control needs admin, so the
# app requests elevation (UAC) when launched.
$ErrorActionPreference = 'Stop'

$repo = 'MateusRed/Open-Fan-Control'
$dir  = Join-Path $env:LOCALAPPDATA 'OpenFanControl'
$exe  = Join-Path $dir 'OpenFanControl.exe'
$url  = "https://github.com/$repo/releases/latest/download/OpenFanControl.exe"

Write-Host "Installing Open Fan Control..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $dir | Out-Null

# Close a running copy so the file isn't locked during update.
Get-Process OpenFanControl -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Downloading latest release..." -ForegroundColor Cyan
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing

# Start-Menu + Desktop shortcuts. The exe manifest requests admin, so UAC prompts on launch.
$ws = New-Object -ComObject WScript.Shell
foreach ($lnk in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Open Fan Control.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop'))  'Open Fan Control.lnk'))) {
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath        = $exe
    $s.WorkingDirectory  = $dir
    $s.IconLocation      = $exe
    $s.Description        = 'Open Fan Control'
    $s.Save()
}

Write-Host "Installed to $exe" -ForegroundColor Green
Write-Host "Opening now (accept the UAC prompt to enable fan control)..." -ForegroundColor Green
Start-Process $exe
