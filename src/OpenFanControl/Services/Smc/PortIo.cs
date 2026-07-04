using System;
using System.Reflection;
using LibreHardwareMonitor.Hardware;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// Byte-wide I/O port access, borrowed from LibreHardwareMonitor's already-loaded
/// WinRing0 kernel driver via reflection (its <c>Ring0</c> class is internal but its
/// I/O methods are public static). We reuse LHM's driver instead of loading our own
/// so there's only ever one WinRing0 instance.
/// </summary>
internal static class PortIo
{
    private static MethodInfo? _read;
    private static MethodInfo? _write;
    private static PropertyInfo? _isOpen;
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var assembly = typeof(Computer).Assembly;
            var ring0 = assembly.GetType("LibreHardwareMonitor.Hardware.Ring0");
            if (ring0 is null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            _read = ring0.GetMethod("ReadIoPort", flags, null, new[] { typeof(uint) }, null);
            _write = ring0.GetMethod("WriteIoPort", flags, null, new[] { typeof(uint), typeof(byte) }, null);
            _isOpen = ring0.GetProperty("IsOpen", flags);
        }
        catch
        {
            _read = null;
            _write = null;
            _isOpen = null;
        }
    }

    /// <summary>True when the underlying kernel driver is available for port access.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            if (_read is null || _write is null) return false;
            try
            {
                return _isOpen is null || (bool)_isOpen.GetValue(null)!;
            }
            catch
            {
                return false;
            }
        }
    }

    public static byte Inb(uint port)
    {
        EnsureInitialized();
        return (byte)_read!.Invoke(null, new object[] { port })!;
    }

    public static void Outb(uint port, byte value)
    {
        EnsureInitialized();
        _write!.Invoke(null, new object[] { port, value });
    }
}
