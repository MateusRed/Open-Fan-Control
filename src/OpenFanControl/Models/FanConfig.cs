using System.Collections.Generic;

namespace OpenFanControl.Models;

/// <summary>Persisted control configuration for a single fan, keyed by its stable identifier.</summary>
public sealed class FanConfig
{
    public string FanIdentifier { get; set; } = string.Empty;

    public FanControlMode Mode { get; set; } = FanControlMode.Auto;

    /// <summary>Target duty cycle for <see cref="FanControlMode.Constant"/> (percent 0-100).</summary>
    public double ConstantPercent { get; set; } = 50;

    /// <summary>Source sensor identifier for <see cref="FanControlMode.SensorBased"/>.</summary>
    public string? SourceSensorIdentifier { get; set; }

    // ---- Legacy 2-point ramp (kept for migration; the graph curve below is now authoritative) ----
    public double MinTemperature { get; set; } = 40;
    public double MaxTemperature { get; set; } = 75;
    public double MinPercent { get; set; } = 20;
    public double MaxPercent { get; set; } = 100;

    /// <summary>
    /// Multi-point graph curve (temperature °C → duty %). Evaluated piecewise-linearly; below
    /// the first point holds the first %, above the last holds the last %.
    /// </summary>
    public List<CurvePoint> CurvePoints { get; set; } = new();

    /// <summary>
    /// Temperature dead-band (°C). The fan follows rising temperatures immediately but only
    /// eases off after the temperature has dropped this far, so it stops hunting on small swings.
    /// </summary>
    public double Hysteresis { get; set; } = 2;

    /// <summary>
    /// Approximate time (seconds) the fan takes to slew toward a new target, smoothing abrupt
    /// jumps. 0 = react instantly.
    /// </summary>
    public double ResponseTimeSeconds { get; set; } = 3;

    // ---- Advanced control tuning (applied to the final duty, in every non-Auto mode) ----

    /// <summary>Hard floor: the fan never runs below this % while running. 0 = no floor.</summary>
    public double MinimumPercent { get; set; } = 0;

    /// <summary>Below this computed %, the fan snaps to 0% (off). 0 = never stop.</summary>
    public double StopPercent { get; set; } = 0;

    /// <summary>Kickstart value briefly applied when starting from a stop (overcomes static friction). 0 = off.</summary>
    public double StartPercent { get; set; } = 0;

    /// <summary>Low edge of a % range the fan should avoid (e.g. a rattly/noisy band).</summary>
    public double AvoidFromPercent { get; set; } = 0;

    /// <summary>High edge of the avoided % range. Active only when greater than <see cref="AvoidFromPercent"/>.</summary>
    public double AvoidToPercent { get; set; } = 0;

    // ---- Trigger mode (two-speed on/off with hysteresis) ----

    /// <summary>Temperature at/below which the fan drops back to idle speed.</summary>
    public double TriggerIdleTemp { get; set; } = 45;

    /// <summary>Temperature at/above which the fan jumps to load speed.</summary>
    public double TriggerLoadTemp { get; set; } = 65;

    public double TriggerIdlePercent { get; set; } = 20;
    public double TriggerLoadPercent { get; set; } = 80;

    /// <summary>Seed the graph curve from the legacy 2-point ramp when it hasn't been set yet.</summary>
    public void EnsureCurve()
    {
        if (CurvePoints.Count >= 2) return;

        CurvePoints = new List<CurvePoint>
        {
            new(MinTemperature, MinPercent),
            new(MaxTemperature, MaxPercent),
        };
    }
}
