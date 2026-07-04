using System;
using System.Text;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// Reads and writes Apple System Management Controller (SMC) keys — the fan speed and
/// temperature keys Macs expose, and the mechanism Macs Fan Control uses to drive MacBook
/// fans under Windows. This class holds the typed key encode/decode logic; the actual bytes
/// go in and out through a pluggable <see cref="ISmcTransport"/> (kernel driver on T2 Macs,
/// legacy I/O ports elsewhere).
///
/// Only ever reach the SMC on genuine Apple hardware — see <see cref="HardwareMonitorService"/>.
/// </summary>
public sealed class AppleSmc : IDisposable
{
    private readonly ISmcTransport _transport;

    public AppleSmc(ISmcTransport transport) => _transport = transport;

    /// <summary>Name of the underlying transport (for diagnostics/UI).</summary>
    public string TransportName => _transport.Name;

    /// <summary>
    /// Picks the best available way to reach the SMC: the kernel driver first (required on
    /// T2 Macs), falling back to legacy I/O ports (pre-T2 Macs / SuperIO). Returns null when
    /// neither is available.
    /// </summary>
    public static AppleSmc? Create()
    {
        var driver = DriverSmcTransport.TryCreate();
        if (driver is not null)
            return new AppleSmc(driver);

        if (PortIo.IsAvailable)
            return new AppleSmc(new PortSmcTransport());

        return null;
    }

    // ---- Key access (delegated to the transport) ----

    public bool TryGetKeyInfo(string key, out SmcKeyInfo info) => _transport.TryGetKeyInfo(key, out info);
    public bool TryReadKey(string key, byte size, out byte[] data) => _transport.TryReadKey(key, size, out data);
    public bool TryWriteKey(string key, byte[] data) => _transport.TryWriteKey(key, data);

    // ---- Typed helpers ----

    /// <summary>Reads a key and decodes it as a numeric value based on its SMC type.</summary>
    public bool TryReadNumber(string key, out double value)
    {
        value = 0;

        if (TryGetKeyInfo(key, out var info) &&
            TryReadKey(key, info.DataSize, out var data) &&
            TryDecode(info.Type, data, out value))
        {
            return true;
        }

        // Fallback: some keys/firmware don't answer the type query cleanly. Try common
        // sizes and decode as a big-endian unsigned integer.
        foreach (byte size in new byte[] { 1, 2, 4 })
        {
            if (TryReadKey(key, size, out var raw))
            {
                ulong n = 0;
                foreach (var b in raw) n = (n << 8) | b;
                if (n is > 0 and < 100000)
                {
                    value = n;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Reads a 4-char-key string value (e.g. a fan's descriptive name).</summary>
    public bool TryReadString(string key, out string text)
    {
        text = string.Empty;
        if (!TryGetKeyInfo(key, out var info)) return false;
        if (!TryReadKey(key, info.DataSize, out var data)) return false;
        text = Encoding.ASCII.GetString(data).TrimEnd('\0', ' ').Trim();
        return true;
    }

    /// <summary>Probes whether a responsive SMC is present by reading the fan-count key.</summary>
    public bool IsPresent() => TryReadNumber("FNum", out var n) && n is >= 0 and < 32;

    public void Dispose() => _transport.Dispose();

    // ---- Type codecs ----

    public static bool TryDecode(string type, byte[] data, out double value)
    {
        value = 0;
        switch (type)
        {
            case "fpe2": // unsigned fixed point, 2 fractional bits (fans / RPM)
                if (data.Length < 2) return false;
                value = ((data[0] << 8) | data[1]) >> 2;
                return true;

            case "fp2e": // rare ordering variant
                if (data.Length < 2) return false;
                value = ((data[0] << 8) | data[1]) / 4.0;
                return true;

            case "flt": // 32-bit IEEE float, little-endian
                if (data.Length < 4) return false;
                value = BitConverter.ToSingle(data, 0);
                return true;

            case "sp78": // signed fixed point, 8 fractional bits (temperatures)
                if (data.Length < 2) return false;
                value = (sbyte)data[0] + data[1] / 256.0;
                return true;

            case "ui8":
            case "ui16":
            case "ui32":
                ulong raw = 0;
                foreach (var b in data) raw = (raw << 8) | b; // big-endian
                value = raw;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Encodes an RPM value into the bytes expected by a fan target key.</summary>
    public static byte[] EncodeFanValue(string type, double rpm)
    {
        switch (type)
        {
            case "flt":
                return BitConverter.GetBytes((float)rpm);

            case "fp2e":
            {
                int r = (int)Math.Round(rpm * 4);
                return new[] { (byte)((r >> 8) & 0xff), (byte)(r & 0xff) };
            }

            case "fpe2":
            default:
            {
                int r = (int)Math.Round(rpm) << 2;
                return new[] { (byte)((r >> 8) & 0xff), (byte)(r & 0xff) };
            }
        }
    }
}
