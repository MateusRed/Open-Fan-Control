using LibreHardwareMonitor.Hardware;

namespace OpenFanControl.Services;

/// <summary>
/// A fan exposed through LibreHardwareMonitor's SuperIO / EC support. Backed by an
/// <see cref="IControl"/> (the duty-cycle output) and, when it can be paired, the
/// matching RPM sensor. Used on regular PCs.
/// </summary>
public sealed class ControllableFan : IControllableFan
{
    private readonly IControl _control;
    private readonly ISensor _controlSensor;
    private readonly ISensor? _rpmSensor;

    internal ControllableFan(ISensor controlSensor, ISensor? rpmSensor)
    {
        _controlSensor = controlSensor;
        _control = controlSensor.Control!;
        _rpmSensor = rpmSensor;

        Identifier = controlSensor.Identifier.ToString();
        HardwareName = controlSensor.Hardware.Name;
        Name = string.IsNullOrWhiteSpace(controlSensor.Name) ? "Fan" : controlSensor.Name;
    }

    public string Identifier { get; }

    public string Name { get; }

    public string HardwareName { get; }

    public bool HasRpm => _rpmSensor is not null;

    public double? Rpm => _rpmSensor?.Value.HasValue == true ? _rpmSensor.Value.Value : null;

    public double? DutyPercent => _controlSensor.Value.HasValue ? _controlSensor.Value.Value : null;

    public double MinPercent => _control.MinSoftwareValue;

    public double MaxPercent => _control.MaxSoftwareValue;

    public void SetSoftware(double percent)
    {
        var clamped = (float)System.Math.Clamp(percent, _control.MinSoftwareValue, _control.MaxSoftwareValue);
        _control.SetSoftware(clamped);
    }

    public void SetDefault() => _control.SetDefault();

    // LibreHardwareMonitor refreshes these sensors during Computer.Update(); nothing to cache.
    public void Poll() { }
}
