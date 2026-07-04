namespace OpenFanControl.Models;

/// <summary>High-level category of a monitored sensor, decoupled from the backing library.</summary>
public enum SensorKind
{
    Temperature,
    Fan,
    Control,
    Load,
    Clock,
    Voltage,
    Power,
    Other
}

/// <summary>How a controllable fan should be driven.</summary>
public enum FanControlMode
{
    /// <summary>Hand control back to the firmware / motherboard (default behaviour).</summary>
    Auto,

    /// <summary>Hold a fixed duty-cycle percentage.</summary>
    Constant,

    /// <summary>Interpolate duty-cycle from a source temperature sensor.</summary>
    SensorBased,

    /// <summary>Two-speed on/off with hysteresis: idle until the load temp, load until back down to idle temp.</summary>
    Trigger
}

/// <summary>Temperature display unit.</summary>
public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}

/// <summary>Kind of user-defined derived sensor.</summary>
public enum CustomSensorType
{
    /// <summary>Combine several sensors with a <see cref="MixFunction"/> (e.g. max of CPU + GPU).</summary>
    Mix,

    /// <summary>Smooth one sensor by averaging it over a time window.</summary>
    TimeAverage
}

/// <summary>How a <see cref="CustomSensorType.Mix"/> sensor combines its inputs.</summary>
public enum MixFunction
{
    Max,
    Min,
    Average,
    Sum,
    Subtract
}
