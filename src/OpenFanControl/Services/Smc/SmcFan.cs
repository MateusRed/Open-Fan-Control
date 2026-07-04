using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// A MacBook / Mac fan driven through the Apple SMC. Speed is expressed to the SMC in
/// RPM, so the app's 0-100% duty maps linearly onto the fan's [min, max] RPM range.
/// </summary>
public sealed class SmcFan : IControllableFan
{
    private readonly AppleSmc _smc;
    private readonly int _index;
    private readonly string _acKey;
    private readonly string _tgKey;
    private readonly string _mdKey;
    private readonly string _targetType;

    private double _rpm;

    private SmcFan(AppleSmc smc, int index, double minRpm, double maxRpm, string targetType, string name)
    {
        _smc = smc;
        _index = index;
        MinRpm = minRpm;
        MaxRpm = maxRpm;
        _targetType = targetType;
        Name = name;

        _acKey = $"F{index}Ac";
        _tgKey = $"F{index}Tg";
        _mdKey = $"F{index}Md";
    }

    public string Identifier => $"/smc/fan/{_index}";
    public string Name { get; }
    public string HardwareName => "Apple SMC";
    public bool HasRpm => true;

    public double MinRpm { get; }
    public double MaxRpm { get; }

    public double? Rpm => _rpm;

    public double? DutyPercent
    {
        get
        {
            if (MaxRpm <= MinRpm) return null;
            return Math.Clamp((_rpm - MinRpm) / (MaxRpm - MinRpm) * 100.0, 0, 100);
        }
    }

    public double MinPercent => 0;
    public double MaxPercent => 100;

    public void Poll()
    {
        if (_smc.TryReadNumber(_acKey, out var rpm))
            _rpm = rpm;
    }

    public void SetSoftware(double percent)
    {
        var pct = Math.Clamp(percent, 0, 100);
        var rpm = MinRpm + pct / 100.0 * (MaxRpm - MinRpm);

        // Force manual mode, then set the target RPM.
        _smc.TryWriteKey(_mdKey, new byte[] { 1 });
        _smc.TryWriteKey(_tgKey, AppleSmc.EncodeFanValue(_targetType, rpm));
    }

    public void SetDefault()
    {
        // Hand the fan back to the firmware's automatic thermal control.
        _smc.TryWriteKey(_mdKey, new byte[] { 0 });
    }

    /// <summary>
    /// Discovers all SMC-controlled fans. Returns an empty list if the SMC has none
    /// or isn't responsive.
    /// </summary>
    public static IReadOnlyList<SmcFan> Enumerate(AppleSmc smc)
    {
        var fans = new List<SmcFan>();

        if (!smc.TryReadNumber("FNum", out var countValue))
            return fans;

        int count = (int)countValue;
        for (int i = 0; i < count && i < 10; i++)
        {
            double min = smc.TryReadNumber($"F{i}Mn", out var mn) ? mn : 0;
            double max = smc.TryReadNumber($"F{i}Mx", out var mx) ? mx : 0;

            // Guard against bogus ranges.
            if (max <= min)
            {
                min = 0;
                max = Math.Max(max, 6000);
            }

            var targetType = smc.TryGetKeyInfo($"F{i}Tg", out var info) ? info.Type : "fpe2";
            var name = ResolveName(smc, i);

            fans.Add(new SmcFan(smc, i, min, max, targetType, name));
        }

        return fans;
    }

    private static string ResolveName(AppleSmc smc, int index)
    {
        // F#ID often holds a human name/location ("Exhaust", "Right Side", …).
        if (smc.TryReadString($"F{index}ID", out var id) && id.Length > 1)
        {
            // The ID payload sometimes has leading control bytes; keep the printable tail.
            var trimmed = new string(id.Where(c => c >= ' ' && c < 127).ToArray()).Trim();
            if (trimmed.Length > 1)
                return trimmed;
        }

        return $"Fan {index + 1}";
    }
}
