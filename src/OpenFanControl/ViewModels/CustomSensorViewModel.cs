using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFanControl.Models;

namespace OpenFanControl.ViewModels;

/// <summary>
/// Editable card for a user-defined custom sensor. Mutates the shared
/// <see cref="CustomSensorConfig"/> in place (the runtime <c>CustomSensor</c> holds it by
/// reference, so edits take effect immediately) and notifies the owner to persist.
/// </summary>
public sealed partial class CustomSensorViewModel : ViewModelBase
{
    private readonly CustomSensorConfig _config;
    private readonly Action _onChanged;
    private readonly Action<CustomSensorViewModel> _onDelete;
    private bool _init;

    [ObservableProperty] private string _name;
    [ObservableProperty] private CustomSensorType _selectedType;
    [ObservableProperty] private MixFunction _selectedFunction;
    [ObservableProperty] private double _averageSeconds;
    [ObservableProperty] private SensorViewModel? _singleSource;

    public CustomSensorViewModel(
        CustomSensorConfig config,
        IReadOnlyList<SensorViewModel> availableSources,
        SensorViewModel displaySensor,
        Action onChanged,
        Action<CustomSensorViewModel> onDelete)
    {
        _config = config;
        _onChanged = onChanged;
        _onDelete = onDelete;
        DisplaySensor = displaySensor;

        _name = config.Name;
        _selectedType = config.Type;
        _selectedFunction = config.Function;
        _averageSeconds = config.AverageSeconds;

        AvailableSources = new ObservableCollection<SensorViewModel>(availableSources);
        SourceOptions = new ObservableCollection<SourceOptionViewModel>(
            availableSources.Select(s => new SourceOptionViewModel(
                s.Identifier, s.Name, s.HardwareName,
                config.SourceIds.Contains(s.Identifier),
                RebuildMixSources)));

        _singleSource = availableSources.FirstOrDefault(s => s.Identifier == config.SourceIds.FirstOrDefault());

        _init = true;
    }

    public CustomSensorConfig Config => _config;
    public string Id => _config.Id;

    /// <summary>Live-value view model (wraps the runtime custom sensor) for the reading display.</summary>
    public SensorViewModel DisplaySensor { get; }

    public ObservableCollection<SensorViewModel> AvailableSources { get; }
    public ObservableCollection<SourceOptionViewModel> SourceOptions { get; }

    public IReadOnlyList<MixFunction> Functions { get; } = new[]
    {
        MixFunction.Max, MixFunction.Min, MixFunction.Average, MixFunction.Sum, MixFunction.Subtract
    };

    public bool IsMix => SelectedType == CustomSensorType.Mix;
    public bool IsTimeAverage => SelectedType == CustomSensorType.TimeAverage;

    partial void OnNameChanged(string value)
    {
        _config.Name = value;
        Commit();
    }

    partial void OnSelectedTypeChanged(CustomSensorType value)
    {
        _config.Type = value;
        OnPropertyChanged(nameof(IsMix));
        OnPropertyChanged(nameof(IsTimeAverage));

        // Keep the config's SourceIds consistent with whichever editor is now shown.
        if (value == CustomSensorType.TimeAverage)
            _config.SourceIds = SingleSource is null ? new List<string>() : new List<string> { SingleSource.Identifier };
        else
            _config.SourceIds = SourceOptions.Where(o => o.IsSelected).Select(o => o.Identifier).ToList();

        Commit();
    }

    partial void OnSelectedFunctionChanged(MixFunction value)
    {
        _config.Function = value;
        Commit();
    }

    partial void OnAverageSecondsChanged(double value)
    {
        _config.AverageSeconds = Math.Round(value);
        Commit();
    }

    partial void OnSingleSourceChanged(SensorViewModel? value)
    {
        if (SelectedType != CustomSensorType.TimeAverage) return;
        _config.SourceIds = value is null ? new List<string>() : new List<string> { value.Identifier };
        Commit();
    }

    private void RebuildMixSources()
    {
        _config.SourceIds = SourceOptions.Where(o => o.IsSelected).Select(o => o.Identifier).ToList();
        Commit();
    }

    [RelayCommand]
    private void Delete() => _onDelete(this);

    private void Commit()
    {
        if (!_init) return;
        _onChanged();
    }
}
