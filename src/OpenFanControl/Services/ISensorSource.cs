using OpenFanControl.Models;

namespace OpenFanControl.Services;

/// <summary>
/// Anything that can act as a sensor reading source — a hardware sensor
/// (<see cref="HardwareSensor"/>) or a user-defined derived sensor
/// (<see cref="CustomSensor"/>). Fans and the UI reference sources uniformly by identifier.
/// </summary>
public interface ISensorSource
{
    /// <summary>Stable identifier, safe to persist.</summary>
    string Identifier { get; }

    string Name { get; }

    string HardwareName { get; }

    SensorKind Kind { get; }

    /// <summary>Current value in the source's native unit (°C for temperatures). Null if unavailable.</summary>
    double? Value { get; }
}
