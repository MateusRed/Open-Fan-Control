using System.Collections.Generic;

namespace OpenFanControl.Models;

/// <summary>
/// A named, self-contained control configuration — its own set of per-fan configs and custom
/// sensors. Users switch profiles to jump between setups (e.g. "Silent" and "Turbo").
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "Default";
    public List<FanConfig> FanConfigs { get; set; } = new();
    public List<CustomSensorConfig> CustomSensors { get; set; } = new();
}
