using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenFanControl.Models;

namespace OpenFanControl.Services;

/// <summary>
/// Translates persisted <see cref="FanConfig"/> entries into concrete duty-cycle
/// commands on the hardware, once per monitoring tick. Sensor-based control runs the
/// configured graph curve through a temperature hysteresis band (so the fan doesn't hunt
/// on small swings) and a response-time slew (so it ramps smoothly). Redundant writes are
/// suppressed so the driver isn't hammered every cycle.
/// </summary>
public sealed class FanController
{
    private readonly HardwareMonitorService _hardware;
    private readonly Dictionary<string, double> _lastPercent = new();
    private readonly HashSet<string> _releasedToAuto = new();

    // Per-fan control state for hysteresis / response-time smoothing.
    private readonly Dictionary<string, double> _effectiveTemp = new();
    private readonly Dictionary<string, double> _smoothedPercent = new();
    private readonly HashSet<string> _stopped = new();
    private readonly HashSet<string> _triggerOn = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastApply;

    public FanController(HardwareMonitorService hardware) => _hardware = hardware;

    /// <summary>Applies each configuration to its fan. Call after <see cref="HardwareMonitorService.Update"/>.</summary>
    public void Apply(IEnumerable<FanConfig> configs)
    {
        var now = _clock.Elapsed;
        double dt = _lastApply == default ? 0 : (now - _lastApply).TotalSeconds;
        _lastApply = now;

        foreach (var cfg in configs)
        {
            var fan = _hardware.FindFan(cfg.FanIdentifier);
            if (fan is null) continue;

            switch (cfg.Mode)
            {
                case FanControlMode.Auto:
                    ApplyAuto(fan);
                    break;

                case FanControlMode.Constant:
                    ApplyPercent(fan, cfg.ConstantPercent, cfg);
                    break;

                case FanControlMode.SensorBased:
                    ApplySensorCurve(fan, cfg, dt);
                    break;

                case FanControlMode.Trigger:
                    ApplyTrigger(fan, cfg, dt);
                    break;
            }
        }
    }

    /// <summary>
    /// Evaluates a graph curve at a given temperature (pure). Below the first point holds the
    /// first %, above the last holds the last %, otherwise piecewise-linear interpolation.
    /// </summary>
    public double EvaluateCurve(FanConfig cfg, double temp)
    {
        var pts = cfg.CurvePoints;
        if (pts is null || pts.Count == 0)
            return cfg.MinPercent;

        var ordered = pts.OrderBy(p => p.Temperature).ToList();
        if (temp <= ordered[0].Temperature) return ordered[0].Percent;
        if (temp >= ordered[^1].Temperature) return ordered[^1].Percent;

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            if (temp >= a.Temperature && temp <= b.Temperature)
            {
                double span = b.Temperature - a.Temperature;
                double r = span < 0.01 ? 0 : (temp - a.Temperature) / span;
                return a.Percent + r * (b.Percent - a.Percent);
            }
        }

        return ordered[^1].Percent;
    }

    /// <summary>The raw target percent the curve would produce right now, for UI display.</summary>
    public double ComputeCurvePercent(FanConfig cfg)
    {
        var temp = _hardware.FindSource(cfg.SourceSensorIdentifier)?.Value;
        return temp is null ? cfg.MinPercent : EvaluateCurve(cfg, temp.Value);
    }

    /// <summary>Current reading (°C) of a config's source sensor, for the live curve marker.</summary>
    public double? CurrentSourceTemp(FanConfig cfg) => _hardware.FindSource(cfg.SourceSensorIdentifier)?.Value;

    private void ApplySensorCurve(IControllableFan fan, FanConfig cfg, double dt)
    {
        var temp = _hardware.FindSource(cfg.SourceSensorIdentifier)?.Value;
        if (temp is null)
        {
            ApplyPercent(fan, 0, cfg);
            return;
        }

        double eff = ApplyHysteresis(fan.Identifier, temp.Value, cfg.Hysteresis);
        double target = EvaluateCurve(cfg, eff);
        double output = ApplyResponseTime(fan.Identifier, fan, target, cfg.ResponseTimeSeconds, dt);
        ApplyPercent(fan, output, cfg);
    }

    private void ApplyTrigger(IControllableFan fan, FanConfig cfg, double dt)
    {
        var temp = _hardware.FindSource(cfg.SourceSensorIdentifier)?.Value;
        if (temp is null)
        {
            ApplyPercent(fan, 0, cfg);
            return;
        }

        bool on = TriggerState(fan.Identifier, cfg, temp.Value);
        double target = on ? cfg.TriggerLoadPercent : cfg.TriggerIdlePercent;
        double output = ApplyResponseTime(fan.Identifier, fan, target, cfg.ResponseTimeSeconds, dt);
        ApplyPercent(fan, output, cfg);
    }

    /// <summary>Updates and returns the on/off state of a trigger fan (load reached vs. cooled to idle).</summary>
    private bool TriggerState(string id, FanConfig cfg, double temp)
    {
        bool on = _triggerOn.Contains(id);
        if (!on && temp >= cfg.TriggerLoadTemp) on = true;
        else if (on && temp <= cfg.TriggerIdleTemp) on = false;

        if (on) _triggerOn.Add(id); else _triggerOn.Remove(id);
        return on;
    }

    /// <summary>Current trigger target % for a config (for UI display), without mutating state.</summary>
    public double TriggerTarget(FanConfig cfg)
    {
        var temp = _hardware.FindSource(cfg.SourceSensorIdentifier)?.Value;
        bool on = temp is not null && (_triggerOn.Contains(cfg.FanIdentifier)
                                       ? temp.Value > cfg.TriggerIdleTemp
                                       : temp.Value >= cfg.TriggerLoadTemp);
        return on ? cfg.TriggerLoadPercent : cfg.TriggerIdlePercent;
    }

    /// <summary>
    /// Rising temperatures track immediately; falling ones only take effect once they've dropped
    /// past the hysteresis band. Prevents the fan from oscillating on small temperature swings.
    /// </summary>
    private double ApplyHysteresis(string id, double temp, double hysteresis)
    {
        if (hysteresis <= 0.01)
        {
            _effectiveTemp[id] = temp;
            return temp;
        }

        double eff = _effectiveTemp.TryGetValue(id, out var e) ? e : temp;
        if (temp >= eff || temp <= eff - hysteresis)
            eff = temp;              // rise, or fall past the band → follow
        // else: within the band → hold the previous effective temperature

        _effectiveTemp[id] = eff;
        return eff;
    }

    /// <summary>Slews the applied percentage toward the target so ~<paramref name="responseTime"/>s is spent converging.</summary>
    private double ApplyResponseTime(string id, IControllableFan fan, double target, double responseTime, double dt)
    {
        if (responseTime <= 0.01 || dt <= 0)
        {
            _smoothedPercent[id] = target;
            return target;
        }

        double current = _smoothedPercent.TryGetValue(id, out var s) ? s : (fan.DutyPercent ?? target);
        double alpha = Math.Clamp(dt / responseTime, 0, 1);
        double next = current + (target - current) * alpha;
        _smoothedPercent[id] = next;
        return next;
    }

    private void ApplyAuto(IControllableFan fan)
    {
        _smoothedPercent.Remove(fan.Identifier);
        _effectiveTemp.Remove(fan.Identifier);
        _stopped.Remove(fan.Identifier);
        _triggerOn.Remove(fan.Identifier);

        if (_releasedToAuto.Contains(fan.Identifier))
            return;

        fan.SetDefault();
        _releasedToAuto.Add(fan.Identifier);
        _lastPercent.Remove(fan.Identifier);
    }

    /// <summary>
    /// Applies the steady-state advanced tuning to a raw duty: steer out of the avoided band,
    /// snap to 0 below the stop threshold, then enforce the floor. Pure (no kickstart / state),
    /// so the UI can show the value that will actually be applied.
    /// </summary>
    public double ApplyTuning(FanConfig cfg, double percent)
    {
        double p = Math.Clamp(percent, 0, 100);

        if (cfg.AvoidToPercent > cfg.AvoidFromPercent &&
            p > cfg.AvoidFromPercent && p < cfg.AvoidToPercent)
        {
            double mid = (cfg.AvoidFromPercent + cfg.AvoidToPercent) / 2.0;
            p = p < mid ? cfg.AvoidFromPercent : cfg.AvoidToPercent;
        }

        if (cfg.StopPercent > 0 && p < cfg.StopPercent) return 0;
        return Math.Max(p, cfg.MinimumPercent);
    }

    private void ApplyPercent(IControllableFan fan, double percent, FanConfig cfg)
    {
        var id = fan.Identifier;
        _releasedToAuto.Remove(id);

        double target = ApplyTuning(cfg, percent);
        bool stop = target <= 0.0;

        // Kickstart: when leaving a stop, briefly apply a stronger nudge for one tick.
        bool wasStopped = _stopped.Contains(id);
        double toIssue = (!stop && wasStopped && cfg.StartPercent > target) ? cfg.StartPercent : target;

        if (stop) _stopped.Add(id); else _stopped.Remove(id);

        toIssue = Math.Clamp(toIssue, fan.MinPercent, fan.MaxPercent);
        if (_lastPercent.TryGetValue(id, out var last) && Math.Abs(last - toIssue) < 0.5)
            return;

        fan.SetSoftware(toIssue);
        _lastPercent[id] = toIssue;
    }

    /// <summary>Releases every fan back to firmware control. Called on shutdown.</summary>
    public void ReleaseAll()
    {
        foreach (var fan in _hardware.Fans)
        {
            try { fan.SetDefault(); }
            catch { /* ignore */ }
        }
    }

    /// <summary>Forget cached state so the next Apply re-issues commands (e.g. after a config edit).</summary>
    public void Invalidate(string fanIdentifier)
    {
        _lastPercent.Remove(fanIdentifier);
        _releasedToAuto.Remove(fanIdentifier);
        _effectiveTemp.Remove(fanIdentifier);
        _smoothedPercent.Remove(fanIdentifier);
        _stopped.Remove(fanIdentifier);
        _triggerOn.Remove(fanIdentifier);
    }

    /// <summary>Clears all per-fan control state (e.g. when switching profiles).</summary>
    public void Reset()
    {
        _lastPercent.Clear();
        _releasedToAuto.Clear();
        _effectiveTemp.Clear();
        _smoothedPercent.Clear();
        _stopped.Clear();
        _triggerOn.Clear();
    }
}
