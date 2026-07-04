namespace OpenFanControl.Services;

/// <summary>
/// A fan the app can drive, regardless of the backing mechanism
/// (SuperIO via LibreHardwareMonitor, or the Apple SMC on Macs).
/// </summary>
public interface IControllableFan
{
    /// <summary>Stable identifier, safe to persist.</summary>
    string Identifier { get; }

    string Name { get; }

    string HardwareName { get; }

    /// <summary>Whether an RPM reading is available.</summary>
    bool HasRpm { get; }

    /// <summary>Current fan speed in RPM, when known.</summary>
    double? Rpm { get; }

    /// <summary>Current duty cycle in percent (0-100), when known.</summary>
    double? DutyPercent { get; }

    double MinPercent { get; }

    double MaxPercent { get; }

    /// <summary>Force a software-controlled duty cycle (percent 0-100).</summary>
    void SetSoftware(double percent);

    /// <summary>Return control to the firmware / automatic mode.</summary>
    void SetDefault();

    /// <summary>
    /// Refresh cached readings. Called once per monitoring tick on the background
    /// thread so UI-thread property reads stay cheap (relevant for SMC fans).
    /// </summary>
    void Poll();
}
