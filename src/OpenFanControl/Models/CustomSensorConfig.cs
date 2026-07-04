using System.Collections.Generic;

namespace OpenFanControl.Models;

/// <summary>
/// A user-defined derived temperature sensor. Either a <see cref="CustomSensorType.Mix"/>
/// combining several source sensors (e.g. "max of CPU + GPU"), or a
/// <see cref="CustomSensorType.TimeAverage"/> smoothing one source over a window.
/// Referenced by fans through the same identifier mechanism as hardware sensors.
/// </summary>
public sealed class CustomSensorConfig
{
    /// <summary>Stable identifier used as a sensor source id (e.g. "custom/&lt;guid&gt;").</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "Custom sensor";

    public CustomSensorType Type { get; set; } = CustomSensorType.Mix;

    /// <summary>Aggregation used when <see cref="Type"/> is <see cref="CustomSensorType.Mix"/>.</summary>
    public MixFunction Function { get; set; } = MixFunction.Max;

    /// <summary>Input sensor identifiers. Mix uses all; TimeAverage uses the first.</summary>
    public List<string> SourceIds { get; set; } = new();

    /// <summary>Averaging window in seconds for <see cref="CustomSensorType.TimeAverage"/>.</summary>
    public double AverageSeconds { get; set; } = 10;
}
