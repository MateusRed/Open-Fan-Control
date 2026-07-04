using CommunityToolkit.Mvvm.ComponentModel;
using OpenFanControl.Models;
using OpenFanControl.Services;

namespace OpenFanControl.ViewModels;

/// <summary>Presents a single sensor reading, formatted for display and refreshed each tick.</summary>
public sealed partial class SensorViewModel : ViewModelBase
{
    private readonly ISensorSource _sensor;

    [ObservableProperty] private string _valueText = "—";
    [ObservableProperty] private double _rawValue;

    public SensorViewModel(ISensorSource sensor, TemperatureUnit unit)
    {
        _sensor = sensor;
        Unit = unit;
        Refresh();
    }

    public string Identifier => _sensor.Identifier;
    public string Name => _sensor.Name;
    public string HardwareName => _sensor.HardwareName;
    public SensorKind Kind => _sensor.Kind;

    public TemperatureUnit Unit { get; set; }

    /// <summary>Short label of the value's unit, e.g. "°C", "RPM", "%".</summary>
    public string UnitLabel => Kind switch
    {
        SensorKind.Temperature => Unit == TemperatureUnit.Celsius ? "°C" : "°F",
        SensorKind.Fan => "RPM",
        SensorKind.Load => "%",
        SensorKind.Control => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Voltage => "V",
        SensorKind.Power => "W",
        _ => string.Empty
    };

    public void Refresh()
    {
        var value = _sensor.Value;
        if (value is null)
        {
            ValueText = "—";
            return;
        }

        double v = value.Value;

        if (Kind == SensorKind.Temperature && Unit == TemperatureUnit.Fahrenheit)
            v = v * 9.0 / 5.0 + 32.0;

        RawValue = v;

        ValueText = Kind switch
        {
            SensorKind.Temperature => $"{v:0}°",
            SensorKind.Fan => $"{v:0}",
            SensorKind.Load => $"{v:0}",
            SensorKind.Clock => $"{v:0}",
            SensorKind.Voltage => $"{v:0.00}",
            SensorKind.Power => $"{v:0.0}",
            _ => $"{v:0.0}"
        };
    }

    /// <summary>Used to populate the "source sensor" picker; temperatures show their current reading.</summary>
    public override string ToString() => $"{Name} ({HardwareName})";
}
