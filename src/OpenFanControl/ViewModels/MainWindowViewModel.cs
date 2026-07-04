using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFanControl.Models;
using OpenFanControl.Services;

namespace OpenFanControl.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly HardwareMonitorService _hardware = new();
    private readonly AutostartService _autostart = new();
    private readonly FanController _controller;
    private readonly AppSettings _settings;
    private Profile _activeProfile;

    private CancellationTokenSource? _cts;
    private volatile bool _dirty;
    private int _saveCountdown;
    private bool _suppressProfile;

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _statusMessage = "Starting…";
    [ObservableProperty] private bool _showAdminWarning;
    [ObservableProperty] private string _headlineValue = "—";
    [ObservableProperty] private string _headlineLabel = "Temperature";
    [ObservableProperty] private string _trayTooltip = "Open Fan Control";
    [ObservableProperty] private bool _isFahrenheit;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _selectedProfile = string.Empty;
    [ObservableProperty] private string _profileName = string.Empty;

    public MainWindowViewModel()
    {
        _controller = new FanController(_hardware);
        _settings = _settingsService.Load();
        _activeProfile = _settings.ActiveProfile();
        _isFahrenheit = _settings.TemperatureUnit == TemperatureUnit.Fahrenheit;
        _startWithWindows = _settings.StartWithWindows;

        // Safety net: never leave fans stuck at a forced speed if the process exits abnormally.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { _controller.ReleaseAll(); } catch { /* ignore */ }
        };
    }

    public ObservableCollection<SensorViewModel> Temperatures { get; } = new();
    public ObservableCollection<FanViewModel> Fans { get; } = new();
    public ObservableCollection<CustomSensorViewModel> CustomSensors { get; } = new();
    public ObservableCollection<string> Profiles { get; } = new();

    public bool HasFans => Fans.Count > 0;

    public bool HasTemperatures => Temperatures.Count > 0;

    public bool HasCustomSensors => CustomSensors.Count > 0;

    public bool ShouldStartMinimized => _settings.StartMinimized;

    public bool MinimizeToTray => _settings.MinimizeToTray;

    public bool CanAutostart => _autostart.IsSupported;

    public string AppVersion =>
        "Open Fan Control " +
        (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

    /// <summary>Opens the hardware layer off the UI thread, then builds the view models.</summary>
    public async Task InitializeAsync()
    {
        StatusMessage = "Reading sensors…";

        await Task.Run(() =>
        {
            _hardware.Open();
            _hardware.Update();
        });

        BuildViewModels();
        RefreshProfiles();

        // Reflect the real logon-task state in the toggle. IsReady is still false here,
        // so OnStartWithWindowsChanged no-ops and we don't re-issue the schtasks command.
        if (_autostart.IsSupported)
        {
            StartWithWindows = _autostart.IsEnabled();
            _settings.StartWithWindows = StartWithWindows;
        }

        ShowAdminWarning = !HardwareMonitorService.IsElevated;
        IsReady = true;
        StatusMessage = HardwareMonitorService.IsElevated
            ? (_hardware.UsingAppleSmc ? "Monitoring · Apple SMC" : "Monitoring")
            : "Read-only — run as administrator to control fans";

        StartLoop();
    }

    private void BuildViewModels()
    {
        Temperatures.Clear();
        foreach (var sensor in _hardware.Temperatures
                     .Where(IsDisplayableTemperature)
                     .OrderBy(s => s.HardwareName)
                     .ThenBy(s => s.Name))
        {
            Temperatures.Add(new SensorViewModel(sensor, _settings.TemperatureUnit));
        }

        BuildProfileViewModels();
    }

    /// <summary>(Re)builds the profile-specific view models (custom sensors + fans) from the active profile.</summary>
    private void BuildProfileViewModels()
    {
        var tempList = Temperatures.ToList();

        // Build custom (derived) sensors from the active profile. The runtime sensors hold their
        // config by reference, so edits made through the view models take effect immediately.
        _hardware.SetCustomSensors(_activeProfile.CustomSensors);
        CustomSensors.Clear();
        foreach (var cfg in _activeProfile.CustomSensors)
        {
            var runtime = _hardware.CustomSensors.FirstOrDefault(c => c.Identifier == cfg.Id);
            if (runtime is null) continue;
            var display = new SensorViewModel(runtime, _settings.TemperatureUnit);
            CustomSensors.Add(new CustomSensorViewModel(cfg, tempList, display, OnCustomSensorChanged, DeleteCustomSensor));
        }

        var sources = SourceList();

        Fans.Clear();
        foreach (var fan in _hardware.Fans)
        {
            var config = _activeProfile.FanConfigs.FirstOrDefault(c => c.FanIdentifier == fan.Identifier);
            if (config is null)
            {
                config = new FanConfig { FanIdentifier = fan.Identifier };
                _activeProfile.FanConfigs.Add(config);
            }

            Fans.Add(new FanViewModel(fan, config, sources, _controller, RequestSave));
        }

        OnPropertyChanged(nameof(HasFans));
        OnPropertyChanged(nameof(HasTemperatures));
        OnPropertyChanged(nameof(HasCustomSensors));
    }

    // ---- Profiles ----

    private void RefreshProfiles()
    {
        _suppressProfile = true;
        Profiles.Clear();
        foreach (var p in _settings.Profiles)
            Profiles.Add(p.Name);
        SelectedProfile = _activeProfile.Name;
        ProfileName = _activeProfile.Name;
        _suppressProfile = false;
    }

    partial void OnSelectedProfileChanged(string value)
    {
        if (_suppressProfile || string.IsNullOrEmpty(value) || value == _activeProfile.Name) return;
        SwitchProfile(value);
    }

    partial void OnProfileNameChanged(string value)
    {
        if (_suppressProfile || string.IsNullOrWhiteSpace(value) || value == _activeProfile.Name) return;
        if (_settings.Profiles.Any(p => p != _activeProfile && p.Name == value)) return; // keep names unique

        _activeProfile.Name = value;
        _settings.ActiveProfileName = value;
        RefreshProfiles();
        RequestSave();
    }

    private void SwitchProfile(string name)
    {
        if (_settings.Profiles.All(p => p.Name != name)) return;

        _settings.ActiveProfileName = name;
        _activeProfile = _settings.ActiveProfile();
        _controller.Reset();
        BuildProfileViewModels();
        RefreshProfiles();
        RequestSave();
    }

    [RelayCommand]
    private void NewProfile()
    {
        var name = UniqueProfileName("Profile");
        _settings.Profiles.Add(new Profile { Name = name });
        SwitchProfile(name);
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        var clone = CloneProfile(_activeProfile);
        clone.Name = UniqueProfileName(_activeProfile.Name + " copy");
        _settings.Profiles.Add(clone);
        SwitchProfile(clone.Name);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (_settings.Profiles.Count <= 1) return;

        _settings.Profiles.Remove(_activeProfile);
        _settings.ActiveProfileName = _settings.Profiles[0].Name;
        _activeProfile = _settings.ActiveProfile();
        _controller.Reset();
        BuildProfileViewModels();
        RefreshProfiles();
        RequestSave();
    }

    private string UniqueProfileName(string baseName)
    {
        if (_settings.Profiles.All(p => p.Name != baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (_settings.Profiles.All(p => p.Name != candidate)) return candidate;
        }
    }

    private static Profile CloneProfile(Profile source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<Profile>(json)!;
    }

    /// <summary>All selectable sources for fans: hardware temperatures plus custom sensors.</summary>
    private IReadOnlyList<SensorViewModel> SourceList() =>
        Temperatures.Concat(CustomSensors.Select(c => c.DisplaySensor)).ToList();

    private void PushSourcesToFans()
    {
        var sources = SourceList();
        foreach (var f in Fans)
            f.UpdateSources(sources);
    }

    [RelayCommand]
    private void AddCustomSensor()
    {
        var cfg = new CustomSensorConfig
        {
            Id = "custom/" + Guid.NewGuid().ToString("N")[..8],
            Name = "Custom sensor",
            Type = CustomSensorType.Mix,
            Function = MixFunction.Max
        };
        _activeProfile.CustomSensors.Add(cfg);

        var runtime = _hardware.AddCustomSensor(cfg);
        var display = new SensorViewModel(runtime, _settings.TemperatureUnit);
        CustomSensors.Add(new CustomSensorViewModel(cfg, Temperatures.ToList(), display, OnCustomSensorChanged, DeleteCustomSensor));

        PushSourcesToFans();
        OnPropertyChanged(nameof(HasCustomSensors));
        RequestSave();
    }

    private void OnCustomSensorChanged() => RequestSave();

    private void DeleteCustomSensor(CustomSensorViewModel vm)
    {
        _activeProfile.CustomSensors.Remove(vm.Config);
        _hardware.RemoveCustomSensor(vm.Id);
        CustomSensors.Remove(vm);

        PushSourcesToFans();
        OnPropertyChanged(nameof(HasCustomSensors));
        RequestSave();
    }

    // "Distance to TjMax" sensors are deltas, not real temperatures — hide them like Macs Fan Control does.
    private static bool IsDisplayableTemperature(HardwareSensor s)
        => !s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase);

    private void StartLoop()
    {
        _cts = new CancellationTokenSource();
        _ = MonitoringLoopAsync(_cts.Token);
    }

    private async Task MonitoringLoopAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(500, _settings.PollIntervalMs));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            try
            {
                _hardware.Update();
                _controller.Apply(_activeProfile.FanConfigs);
            }
            catch
            {
                // A transient driver hiccup shouldn't kill the loop.
            }

            await Dispatcher.UIThread.InvokeAsync(RefreshUi);

            MaybeSave();
        }
    }

    private void RefreshUi()
    {
        foreach (var t in Temperatures) t.Refresh();
        foreach (var c in CustomSensors) c.DisplaySensor.Refresh();
        foreach (var f in Fans) f.Refresh();
        UpdateHeadline();
    }

    private void UpdateHeadline()
    {
        // Prefer the hottest CPU/GPU package temperature for the headline.
        var hottest = Temperatures
            .Where(t => t.RawValue > 0)
            .OrderByDescending(t => t.RawValue)
            .FirstOrDefault();

        if (hottest is null)
        {
            HeadlineValue = "—";
            return;
        }

        HeadlineValue = hottest.ValueText + hottest.UnitLabel;
        HeadlineLabel = hottest.Name;

        var rpm = Fans.Select(f => f.HasRpm ? f : null).FirstOrDefault(f => f is not null);
        TrayTooltip = rpm is null
            ? $"Open Fan Control — {HeadlineLabel} {HeadlineValue}"
            : $"Open Fan Control — {HeadlineLabel} {HeadlineValue}, {rpm.RpmText}";
    }

    private void RequestSave() => _dirty = true;

    private void MaybeSave()
    {
        if (!_dirty) return;
        if (--_saveCountdown > 0) return;

        _dirty = false;
        _saveCountdown = 2;
        _settings.TemperatureUnit = IsFahrenheit ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
        _settingsService.Save(_settings);
    }

    partial void OnIsFahrenheitChanged(bool value)
    {
        var unit = value ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
        foreach (var t in Temperatures)
        {
            t.Unit = unit;
            t.Refresh();
        }
        foreach (var c in CustomSensors)
        {
            c.DisplaySensor.Unit = unit;
            c.DisplaySensor.Refresh();
        }
        _settings.TemperatureUnit = unit;
        RequestSave();
        UpdateHeadline();
    }

    [RelayCommand]
    private void ToggleTemperatureUnit() => IsFahrenheit = !IsFahrenheit;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!IsReady) return; // ignore the initial reconcile pass
        _autostart.Apply(value);
        _settings.StartWithWindows = value;
        RequestSave();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }

        // Hand every fan back to firmware and persist before exit.
        try { _controller.ReleaseAll(); } catch { /* ignore */ }

        _settings.TemperatureUnit = IsFahrenheit ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
        _settingsService.Save(_settings);

        _hardware.Dispose();
        _cts?.Dispose();
    }
}
