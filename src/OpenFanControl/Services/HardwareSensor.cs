using LibreHardwareMonitor.Hardware;
using OpenFanControl.Models;

namespace OpenFanControl.Services;

/// <summary>
/// Read-only wrapper around a single <see cref="ISensor"/> exposing a stable id and
/// a friendly display value. Values are read live from the backing sensor.
/// </summary>
public sealed class HardwareSensor : ISensorSource
{
    private readonly ISensor _sensor;

    internal HardwareSensor(ISensor sensor)
    {
        _sensor = sensor;
        Identifier = sensor.Identifier.ToString();
        Name = sensor.Name;
        HardwareName = sensor.Hardware.Name;
        HardwareType = sensor.Hardware.HardwareType.ToString();
        Kind = Map(sensor.SensorType);
    }

    /// <summary>Stable identifier, safe to persist across sessions.</summary>
    public string Identifier { get; }

    public string Name { get; }

    public string HardwareName { get; }

    public string HardwareType { get; }

    public SensorKind Kind { get; }

    /// <summary>Current value in the sensor's native unit (°C, RPM, %, …). Null if unavailable.</summary>
    public double? Value => _sensor.Value.HasValue ? _sensor.Value.Value : null;

    public double? Min => _sensor.Min.HasValue ? _sensor.Min.Value : null;

    public double? Max => _sensor.Max.HasValue ? _sensor.Max.Value : null;

    internal static SensorKind Map(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Power => SensorKind.Power,
        _ => SensorKind.Other
    };
}
