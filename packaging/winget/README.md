# winget packaging (optional, for later)

The primary quick-install is the one-liner in the main README (`scripts/install.ps1`).
Once you've published a GitHub Release, you can also submit Open Fan Control to
[winget-pkgs](https://github.com/microsoft/winget-pkgs) so users can `winget install OpenFanControl`.

The app ships as a single portable `.exe`, so use the **portable** installer type. Template
(fill in `__PUBLISHER__`, version, the release URL and its SHA256):

`OpenFanControl.installer.yaml`
```yaml
PackageIdentifier: __PUBLISHER__.OpenFanControl
PackageVersion: 1.0.0
InstallerType: portable
Commands:
  - openfancontrol
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/MateusRed/Open-Fan-Control/releases/download/v1.0.0/OpenFanControl.exe
    InstallerSha256: <sha256 of the exe>
ManifestType: installer
ManifestVersion: 1.6.0
```

Get the SHA256 after a release:

```powershell
(Get-FileHash .\OpenFanControl.exe -Algorithm SHA256).Hash
```

A full submission also needs `OpenFanControl.locale.en-US.yaml` (name, publisher, license,
description) and `OpenFanControl.yaml` (version). See the winget-pkgs docs, or use
[`wingetcreate`](https://github.com/microsoft/winget-create):

```powershell
winget install wingetcreate
wingetcreate new https://github.com/MateusRed/Open-Fan-Control/releases/download/v1.0.0/OpenFanControl.exe
```
