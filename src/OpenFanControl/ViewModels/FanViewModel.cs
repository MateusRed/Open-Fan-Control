using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFanControl.Models;
using OpenFanControl.Services;

namespace OpenFanControl.ViewModels;

/// <summary>
/// Drives a single fan: shows its live RPM / duty and exposes editable control settings
/// (Auto, Constant, Sensor-based graph curve) that are written straight back into the
/// persisted config. The sensor-based mode uses a multi-point curve plus hysteresis and
/// response-time smoothing.
/// </summary>
public sealed partial class FanViewModel : ViewModelBase
{
    private readonly IControllableFan _fan;
    private readonly FanConfig _config;
    private readonly FanController _controller;
    private readonly Action _requestSave;
    private readonly bool _initialized;

    [ObservableProperty] private FanControlMode _selectedMode;
    [ObservableProperty] private double _constantPercent;
    [ObservableProperty] private SensorViewModel? _sourceSensor;
    [ObservableProperty] private double _hysteresis;
    [ObservableProperty] private double _responseTimeSeconds;
    [ObservableProperty] private double _minimumPercent;
    [ObservableProperty] private double _stopPercent;
    [ObservableProperty] private double _startPercent;
    [ObservableProperty] private double _avoidFromPercent;
    [ObservableProperty] private double _avoidToPercent;
    [ObservableProperty] private double _triggerIdleTemp;
    [ObservableProperty] private double _triggerLoadTemp;
    [ObservableProperty] private double _triggerIdlePercent;
    [ObservableProperty] private double _triggerLoadPercent;

    [ObservableProperty] private double? _currentTemperature;
    [ObservableProperty] private double? _currentPercent;

    [ObservableProperty] private string _rpmText = "—";
    [ObservableProperty] private string _dutyText = "—";
    [ObservableProperty] private string _targetText = string.Empty;

    public FanViewModel(
        IControllableFan fan,
        FanConfig config,
        IReadOnlyList<SensorViewModel> temperatureSensors,
        FanController controller,
        Action requestSave)
    {
        _fan = fan;
        _config = config;
        _controller = controller;
        _requestSave = requestSave;

        AvailableSensors = new ObservableCollection<SensorViewModel>(temperatureSensors);

        _selectedMode = config.Mode;
        _constantPercent = config.ConstantPercent;
        _hysteresis = config.Hysteresis;
        _responseTimeSeconds = config.ResponseTimeSeconds;
        _minimumPercent = config.MinimumPercent;
        _stopPercent = config.StopPercent;
        _startPercent = config.StartPercent;
        _avoidFromPercent = config.AvoidFromPercent;
        _avoidToPercent = config.AvoidToPercent;
        _triggerIdleTemp = config.TriggerIdleTemp;
        _triggerLoadTemp = config.TriggerLoadTemp;
        _triggerIdlePercent = config.TriggerIdlePercent;
        _triggerLoadPercent = config.TriggerLoadPercent;
        _sourceSensor = AvailableSensors.FirstOrDefault(s => s.Identifier == config.SourceSensorIdentifier)
                        ?? AvailableSensors.FirstOrDefault();

        // Persist the defaulted source so the curve marker and control use it from the first run.
        if (config.SourceSensorIdentifier is null && _sourceSensor is not null)
            config.SourceSensorIdentifier = _sourceSensor.Identifier;

        _config.EnsureCurve();
        CurvePoints = new ObservableCollection<CurvePointViewModel>(
            _config.CurvePoints.OrderBy(p => p.Temperature)
                               .Select(p => new CurvePointViewModel(p.Temperature, p.Percent)));

        _initialized = true;
        Refresh();
    }

    public string Identifier => _fan.Identifier;
    public string Name => _fan.Name;
    public string HardwareName => _fan.HardwareName;
    public bool HasRpm => _fan.HasRpm;

    public ObservableCollection<SensorViewModel> AvailableSensors { get; }
    public ObservableCollection<CurvePointViewModel> CurvePoints { get; }

    /// <summary>Replace the available source list (e.g. after custom sensors change), keeping the current selection.</summary>
    public void UpdateSources(IReadOnlyList<SensorViewModel> sources)
    {
        var currentId = SourceSensor?.Identifier ?? _config.SourceSensorIdentifier;
        AvailableSensors.Clear();
        foreach (var s in sources)
            AvailableSensors.Add(s);

        // Same instance if the selection still exists; null if it was removed.
        SourceSensor = AvailableSensors.FirstOrDefault(s => s.Identifier == currentId);
    }

    public IReadOnlyList<FanControlMode> Modes { get; } = new[]
    {
        FanControlMode.Auto,
        FanControlMode.Constant,
        FanControlMode.SensorBased
    };

    public bool IsConstant => SelectedMode == FanControlMode.Constant;
    public bool IsSensorBased => SelectedMode == FanControlMode.SensorBased;
    public bool IsTrigger => SelectedMode == FanControlMode.Trigger;

    /// <summary>Advanced tuning applies to any software-driven mode (not Auto).</summary>
    public bool IsControlled => SelectedMode != FanControlMode.Auto;

    public void Refresh()
    {
        RpmText = _fan.Rpm is { } rpm ? $"{rpm:0} RPM" : (HasRpm ? "— RPM" : "no RPM sensor");
        DutyText = _fan.DutyPercent is { } duty ? $"{duty:0}%" : "—";

        TargetText = SelectedMode switch
        {
            FanControlMode.Auto => "Firmware controlled",
            FanControlMode.Constant => $"Target {_controller.ApplyTuning(_config, ConstantPercent):0}%",
            FanControlMode.SensorBased => $"Target {_controller.ApplyTuning(_config, _controller.ComputeCurvePercent(_config)):0}%",
            FanControlMode.Trigger => $"Target {_controller.ApplyTuning(_config, _controller.TriggerTarget(_config)):0}%",
            _ => string.Empty
        };

        // Live operating point for the curve editor marker (temperature is always °C).
        var temp = _controller.CurrentSourceTemp(_config);
        CurrentTemperature = temp;
        CurrentPercent = temp is null ? null : _controller.EvaluateCurve(_config, temp.Value);
    }

    partial void OnSelectedModeChanged(FanControlMode value)
    {
        OnPropertyChanged(nameof(IsConstant));
        OnPropertyChanged(nameof(IsSensorBased));
        OnPropertyChanged(nameof(IsTrigger));
        OnPropertyChanged(nameof(IsControlled));
        Sync();
    }

    partial void OnConstantPercentChanged(double value) => Sync();
    partial void OnSourceSensorChanged(SensorViewModel? value) => Sync();
    partial void OnHysteresisChanged(double value) => Sync();
    partial void OnResponseTimeSecondsChanged(double value) => Sync();
    partial void OnMinimumPercentChanged(double value) => Sync();
    partial void OnStopPercentChanged(double value) => Sync();
    partial void OnStartPercentChanged(double value) => Sync();
    partial void OnAvoidFromPercentChanged(double value) => Sync();
    partial void OnAvoidToPercentChanged(double value) => Sync();
    partial void OnTriggerIdleTempChanged(double value) => Sync();
    partial void OnTriggerLoadTempChanged(double value) => Sync();
    partial void OnTriggerIdlePercentChanged(double value) => Sync();
    partial void OnTriggerLoadPercentChanged(double value) => Sync();

    /// <summary>Called by the curve editor after a point is added, moved or removed.</summary>
    [RelayCommand]
    private void CurveChanged()
    {
        if (!_initialized) return;

        _config.CurvePoints = CurvePoints
            .OrderBy(p => p.Temperature)
            .Select(p => new CurvePoint(Math.Round(p.Temperature), Math.Round(p.Percent)))
            .ToList();

        _controller.Invalidate(_fan.Identifier);
        _requestSave();
        Refresh();
    }

    private void Sync()
    {
        if (!_initialized) return;

        _config.Mode = SelectedMode;
        _config.ConstantPercent = Math.Round(ConstantPercent);
        _config.SourceSensorIdentifier = SourceSensor?.Identifier;
        _config.Hysteresis = Math.Round(Hysteresis, 1);
        _config.ResponseTimeSeconds = Math.Round(ResponseTimeSeconds, 1);
        _config.MinimumPercent = Math.Round(MinimumPercent);
        _config.StopPercent = Math.Round(StopPercent);
        _config.StartPercent = Math.Round(StartPercent);
        _config.AvoidFromPercent = Math.Round(AvoidFromPercent);
        _config.AvoidToPercent = Math.Round(AvoidToPercent);
        _config.TriggerIdleTemp = Math.Round(TriggerIdleTemp);
        _config.TriggerLoadTemp = Math.Round(TriggerLoadTemp);
        _config.TriggerIdlePercent = Math.Round(TriggerIdlePercent);
        _config.TriggerLoadPercent = Math.Round(TriggerLoadPercent);

        _controller.Invalidate(_fan.Identifier);
        _requestSave();
        Refresh();
    }
}
