namespace OpenFanControl.Models;

/// <summary>A single point on a fan curve: at <see cref="Temperature"/> °C, run at <see cref="Percent"/> duty.</summary>
public sealed class CurvePoint
{
    public double Temperature { get; set; }
    public double Percent { get; set; }

    public CurvePoint() { }

    public CurvePoint(double temperature, double percent)
    {
        Temperature = temperature;
        Percent = percent;
    }
}
