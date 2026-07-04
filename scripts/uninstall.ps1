# Open Fan Control — uninstaller. Removes the app and shortcuts.
# Settings in %AppData%\OpenFanControl are kept (delete that folder too for a clean wipe).
#
#   irm https://raw.githubusercontent.com/MateusRed/Open-Fan-Control/main/scripts/uninstall.ps1 | iex
$ErrorActionPreference = 'SilentlyContinue'

Get-Process OpenFanControl | Stop-Process -Force

Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'OpenFanControl')
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath('Programs')) 'Open Fan Control.lnk')
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath('Desktop'))  'Open Fan Control.lnk')

Write-Host "Open Fan Control removed. Settings in %AppData%\OpenFanControl were kept." -ForegroundColor Green
