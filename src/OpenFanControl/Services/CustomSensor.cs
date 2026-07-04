using System;
using System.Collections.Generic;
using System.Linq;
using OpenFanControl.Models;

namespace OpenFanControl.Services;

/// <summary>
/// Runtime evaluation of a <see cref="CustomSensorConfig"/> as a live sensor source. Holds the
/// config by reference, so edits to it are reflected immediately without rebuilding. Mix values
/// are computed on read; time-averages are sampled once per monitoring tick via <see cref="Sample"/>.
/// </summary>
public sealed class CustomSensor : ISensorSource
{
    private readonly CustomSensorConfig _config;
    private readonly Func<string, ISensorSource?> _resolve;
    private readonly Queue<(double time, double value)> _samples = new();
    private double? _averaged;

    public CustomSensor(CustomSensorConfig config, Func<string, ISensorSource?> resolve)
    {
        _config = config;
        _resolve = resolve;
    }

    public string Identifier => _config.Id;
    public string Name => _config.Name;
    public string HardwareName => _config.Type == CustomSensorType.TimeAverage ? "Time average" : "Mix";
    public SensorKind Kind => SensorKind.Temperature;

    public double? Value => _config.Type == CustomSensorType.TimeAverage ? _averaged : ComputeMix();

    /// <summary>Pushes a fresh reading into the time-average window. Called once per tick.</summary>
    public void Sample(double nowSeconds)
    {
        if (_config.Type != CustomSensorType.TimeAverage) return;

        var sourceId = _config.SourceIds.FirstOrDefault();
        var v = sourceId is null ? null : _resolve(sourceId)?.Value;
        if (v.HasValue)
            _samples.Enqueue((nowSeconds, v.Value));

        double window = Math.Max(1, _config.AverageSeconds);
        while (_samples.Count > 0 && nowSeconds - _samples.Peek().time > window)
            _samples.Dequeue();

        _averaged = _samples.Count > 0 ? _samples.Average(s => s.value) : null;
    }

    private double? ComputeMix()
    {
        var values = _config.SourceIds
            .Select(id => _resolve(id)?.Value)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count == 0) return null;

        return _config.Function switch
        {
            MixFunction.Max => values.Max(),
            MixFunction.Min => values.Min(),
            MixFunction.Average => values.Average(),
            MixFunction.Sum => values.Sum(),
            MixFunction.Subtract => values.Skip(1).Aggregate(values[0], (acc, v) => acc - v),
            _ => values.Max()
        };
    }
}
