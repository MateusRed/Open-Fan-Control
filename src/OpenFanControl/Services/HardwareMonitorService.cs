using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using OpenFanControl.Models;
using OpenFanControl.Services.Smc;

namespace OpenFanControl.Services;

/// <summary>
/// Central access point to the machine's sensors and controllable fans.
/// Wraps a single <see cref="Computer"/> instance from LibreHardwareMonitor.
/// All hardware access is expected to happen on one thread (the monitoring loop).
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly List<CustomSensor> _customSensors = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private bool _opened;
    private AppleSmc? _smc;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false
        };
    }

    /// <summary>All discovered sensors (temperatures, fans, loads, …).</summary>
    public IReadOnlyList<HardwareSensor> Sensors { get; private set; } = Array.Empty<HardwareSensor>();

    /// <summary>Sensors the UI treats as "temperatures".</summary>
    public IReadOnlyList<HardwareSensor> Temperatures =>
        Sensors.Where(s => s.Kind == Models.SensorKind.Temperature).ToList();

    /// <summary>User-defined derived sensors (mixes, time-averages).</summary>
    public IReadOnlyList<CustomSensor> CustomSensors => _customSensors;

    /// <summary>Fans the app is able to drive.</summary>
    public IReadOnlyList<IControllableFan> Fans { get; private set; } = Array.Empty<IControllableFan>();

    /// <summary>True when fan control is going through the Apple SMC (Mac hardware).</summary>
    public bool UsingAppleSmc { get; private set; }

    /// <summary>True when the process holds administrator rights (required for control).</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Opens the hardware layer and enumerates devices. Safe to call once.</summary>
    public void Open()
    {
        if (_opened) return;
        _computer.Open();
        _opened = true;
        RebuildDeviceLists();
    }

    /// <summary>Refreshes every sensor value. Call from the monitoring loop.</summary>
    public void Update()
    {
        if (!_opened) return;
        _computer.Accept(_visitor);

        // Refresh cached SMC readings on this background thread.
        foreach (var fan in Fans)
            fan.Poll();

        // Sample time-average custom sensors after hardware values are fresh.
        double now = _clock.Elapsed.TotalSeconds;
        foreach (var cs in _customSensors)
            cs.Sample(now);
    }

    /// <summary>Look up a temperature sensor by its persisted identifier.</summary>
    public HardwareSensor? FindSensor(string? identifier) =>
        identifier is null ? null : Sensors.FirstOrDefault(s => s.Identifier == identifier);

    /// <summary>Resolve any source (hardware sensor or custom sensor) by identifier.</summary>
    public ISensorSource? FindSource(string? identifier)
    {
        if (identifier is null) return null;
        return (ISensorSource?)Sensors.FirstOrDefault(s => s.Identifier == identifier)
               ?? _customSensors.FirstOrDefault(c => c.Identifier == identifier);
    }

    /// <summary>Rebuild the custom-sensor set from configs (called once at startup).</summary>
    public void SetCustomSensors(IEnumerable<CustomSensorConfig> configs)
    {
        _customSensors.Clear();
        foreach (var config in configs)
            _customSensors.Add(new CustomSensor(config, FindSource));
    }

    /// <summary>Add one custom sensor at runtime (its config is held by reference).</summary>
    public CustomSensor AddCustomSensor(CustomSensorConfig config)
    {
        var sensor = new CustomSensor(config, FindSource);
        _customSensors.Add(sensor);
        return sensor;
    }

    /// <summary>Remove a custom sensor by identifier.</summary>
    public void RemoveCustomSensor(string identifier) =>
        _customSensors.RemoveAll(c => c.Identifier == identifier);

    public IControllableFan? FindFan(string? identifier) =>
        identifier is null ? null : Fans.FirstOrDefault(f => f.Identifier == identifier);

    private void RebuildDeviceLists()
    {
        var sensors = new List<HardwareSensor>();
        var controls = new List<ISensor>();
        var fanSensors = new List<ISensor>();

        foreach (var hw in EnumerateHardware(_computer.Hardware))
        {
            foreach (var sensor in hw.Sensors)
            {
                sensors.Add(new HardwareSensor(sensor));

                if (sensor.SensorType == SensorType.Control && sensor.Control is not null)
                    controls.Add(sensor);
                else if (sensor.SensorType == SensorType.Fan)
                    fanSensors.Add(sensor);
            }
        }

        Sensors = sensors;

        var fans = new List<IControllableFan>(PairFans(controls, fanSensors));
        fans.AddRange(DiscoverSmcFans());
        Fans = fans;
    }

    /// <summary>
    /// On genuine Apple hardware, fans are driven through the SMC rather than a SuperIO
    /// chip. This is what lets us control MacBook fans under Windows (like Macs Fan
    /// Control). The Apple-hardware gate ensures we never touch these I/O ports on a PC.
    /// </summary>
    private IReadOnlyList<SmcFan> DiscoverSmcFans()
    {
        SmcDiagnostics.Reset();
        try
        {
            bool apple = AppleHardware.IsApple();
            SmcDiagnostics.Log($"IsApple={apple}  PortIo.IsAvailable={PortIo.IsAvailable}");

            if (!apple)
                return Array.Empty<SmcFan>();

            // A snapshot of the legacy port window helps tell "no driver" from "T2-mediated"
            // (all 0xFF) when reading the debug log.
            if (PortIo.IsAvailable)
            {
                try
                {
                    SmcDiagnostics.Log("Command-port status samples (0x304): " + PortSmcTransport.SampleStatus());
                    SmcDiagnostics.Log(PortSmcTransport.DumpPorts());
                }
                catch (Exception ex) { SmcDiagnostics.Log("Port dump failed: " + ex.Message); }
            }

            // Prefer the kernel driver (required on T2 Macs), fall back to port I/O.
            _smc = AppleSmc.Create();
            if (_smc is null)
            {
                SmcDiagnostics.Log("No SMC transport available (no AppleSMC driver, no port I/O) — aborting.");
                return Array.Empty<SmcFan>();
            }
            SmcDiagnostics.Log("SMC transport: " + _smc.TransportName);

            bool typeOk = _smc.TryGetKeyInfo("FNum", out var fnumInfo);
            SmcDiagnostics.Log($"FNum key-type read: ok={typeOk} type='{fnumInfo.Type}' size={fnumInfo.DataSize}");

            bool numOk = _smc.TryReadNumber("FNum", out var fnum);
            SmcDiagnostics.Log($"FNum value read: ok={numOk} value={fnum}");

            if (!_smc.IsPresent())
            {
                SmcDiagnostics.Log("SMC not present/responsive — aborting.");
                return Array.Empty<SmcFan>();
            }

            var smcFans = SmcFan.Enumerate(_smc);
            SmcDiagnostics.Log($"Enumerated {smcFans.Count} SMC fan(s).");
            foreach (var f in smcFans)
                SmcDiagnostics.Log($"  {f.Identifier} '{f.Name}' min={f.MinRpm} max={f.MaxRpm} rpm={f.Rpm}");

            UsingAppleSmc = smcFans.Count > 0;
            return smcFans;
        }
        catch (Exception ex)
        {
            SmcDiagnostics.Log("EXCEPTION: " + ex);
            return Array.Empty<SmcFan>();
        }
    }

    /// <summary>
    /// Pairs each control (duty output) with a fan (RPM) sensor. Controls and fans
    /// on the same hardware are matched by the trailing number in their name, then by
    /// position as a fallback. This mirrors how SuperIO chips expose "Fan Control #n"
    /// alongside "Fan #n".
    /// </summary>
    private static IReadOnlyList<ControllableFan> PairFans(List<ISensor> controls, List<ISensor> fanSensors)
    {
        var result = new List<ControllableFan>();
        var usedFans = new HashSet<ISensor>();

        foreach (var control in controls.OrderBy(c => c.Hardware.Name).ThenBy(c => c.Index))
        {
            var siblings = fanSensors
                .Where(f => ReferenceEquals(f.Hardware, control.Hardware) && !usedFans.Contains(f))
                .ToList();

            ISensor? match = null;

            var controlNumber = ExtractNumber(control.Name);
            if (controlNumber is not null)
                match = siblings.FirstOrDefault(f => ExtractNumber(f.Name) == controlNumber);

            match ??= siblings.OrderBy(f => f.Index).FirstOrDefault(f => f.Index == control.Index);
            match ??= siblings.OrderBy(f => f.Index).FirstOrDefault();

            if (match is not null)
                usedFans.Add(match);

            result.Add(new ControllableFan(control, match));
        }

        return result;
    }

    private static int? ExtractNumber(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : null;
    }

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> roots)
    {
        foreach (var hw in roots)
        {
            yield return hw;
            foreach (var sub in EnumerateHardware(hw.SubHardware))
                yield return sub;
        }
    }

    public void Dispose()
    {
        if (!_opened) return;
        try { _computer.Close(); }
        catch { /* ignore driver teardown errors */ }
        _opened = false;
    }
}
