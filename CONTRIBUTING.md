# Contributing to Open Fan Control

Thanks for your interest in improving Open Fan Control! This is a small, focused project and
contributions of all sizes are welcome — from typo fixes to new curve types to **hardware
compatibility reports** (which are especially valuable for a fan-control app).

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).

## Ways to contribute

- 🐛 **Report a bug** — open an issue with steps to reproduce.
- 🖥️ **Report hardware compatibility** — tell us whether monitoring/control worked on your
  machine (Mac model or motherboard). See [Hardware reports](#hardware-compatibility-reports).
- ✨ **Propose a feature** — open an issue to discuss before large changes.
- 🔧 **Send a pull request** — bug fixes, features, docs.

## Development setup

**Prerequisites**

- Windows 10 / 11 (x64) — the app is Windows-only (it drives Windows kernel drivers).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

**Build & run**

```powershell
git clone https://github.com/MateusRed/Open-Fan-Control.git
cd Open-Fan-Control
dotnet build src/OpenFanControl/OpenFanControl.csproj -c Release
```

The app requests **administrator** rights at runtime (fan control needs kernel drivers), so a
normal `dotnet run` triggers a UAC prompt. Two handy ways to run while developing:

```powershell
# Full run, elevated (UAC prompt) — needed to actually control fans:
powershell -ExecutionPolicy Bypass -File .\run-admin.ps1

# UI-only iteration without a UAC prompt (runs as invoker via the .NET host).
# Sensors/fan control are read-only here, but it's great for working on the interface:
dotnet build src/OpenFanControl/OpenFanControl.csproj -c Release
dotnet src/OpenFanControl/bin/Release/net8.0-windows/OpenFanControl.dll
```

**Useful paths**

- Settings: `%AppData%\OpenFanControl\settings.json`
- SMC diagnostics log (Mac fan detection): `%AppData%\OpenFanControl\smc-debug.log`

## ⚠️ Testing fan control safely

You're commanding real fans, so:

- **Raising** a fan (more cooling) is safe; **lowering** it under load can let components
  overheat. Test low speeds only at idle, and watch temperatures.
- The app always releases fans back to firmware **Auto** on quit/crash, and rising
  temperatures take effect immediately even mid-curve — please keep both invariants intact.
- Prefer testing with a single fan set to **Constant**, then verify curves/triggers.

## Project layout

See the [README](README.md#how-it-works) for the high-level design. Quick map:

```
src/OpenFanControl/
├── Models/        Persisted config: AppSettings, Profile, FanConfig, CurvePoint, CustomSensorConfig, enums
├── Services/      HardwareMonitorService, FanController, SettingsService, sensors…
│   └── Smc/       Apple SMC access (ISmcTransport: port I/O + \\.\APPLESMC driver)
├── ViewModels/    MVVM (CommunityToolkit.Mvvm)
└── Views/         MainWindow.axaml + CurveEditor (custom-rendered graph control)
```

## Where common changes go

- **New control mode / curve type** — add to `FanControlMode`, a branch in
  `FanController.Apply`, fields on `FanConfig`, view-model state in `FanViewModel`, and a panel
  in `MainWindow.axaml` (see how `Trigger` is wired end-to-end as a template).
- **New custom sensor type** — extend `CustomSensorType`, handle it in `CustomSensor`, and add
  UI in `CustomSensorViewModel` + the sidebar section.
- **SMC / Apple hardware** — implement or adjust an `ISmcTransport`. Anything above the
  transport is hardware-agnostic. Keep the SMBIOS "Apple Inc." gate so SMC ports are never
  touched on a PC.
- **More PC hardware support** — usually comes from
  [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor); bump the
  package or report the gap upstream.

## Coding conventions

- **Match the surrounding style.** Nullable is enabled; keep it warning-free.
- Follow the existing comment density — short comments that explain *why*, not *what*.
- MVVM: use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- Keep hardware access on the monitoring loop thread (see `HardwareMonitorService`).
- Run `dotnet build -c Release` before pushing — CI builds on every PR and must stay green.

## Pull requests

1. Fork and create a branch: `git checkout -b feature/short-description`.
2. Keep PRs focused — one logical change per PR is easier to review.
3. Describe **what** and **why**, and mention the hardware you tested on.
4. Make sure the build passes (the **Build** GitHub Action runs automatically).

## Hardware compatibility reports

These help everyone. When opening a report, include:

- Machine: Mac model (e.g. *MacBook Pro 16" 2019, T2*) **or** motherboard/laptop model.
- What worked: temperatures shown? fans detected? fan control functional?
- For Macs: attach `%AppData%\OpenFanControl\smc-debug.log`.
- Whether you're running as administrator, and (T2 Macs) whether Macs Fan Control is installed.

Thanks for helping make Open Fan Control better! 🌀
