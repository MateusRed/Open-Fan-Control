using System;

namespace OpenFanControl.Services.Smc;

/// <summary>Metadata for an SMC key: its payload length and 4-char type code (e.g. "fpe2").</summary>
public readonly record struct SmcKeyInfo(byte DataSize, string Type);

/// <summary>
/// A way to talk to the Apple SMC at the key level (read/write named 4-char keys).
///
/// There are two ways in: legacy I/O ports 0x300/0x304 (<see cref="PortSmcTransport"/>,
/// works on pre-T2 Intel Macs and PC SuperIO), and a kernel driver exposing the SMC over
/// MMIO (<see cref="DriverSmcTransport"/>, the only way in on T2 Macs where the legacy
/// ports read back 0xFF). Both speak the same applesmc command protocol; only the plumbing
/// underneath differs, so everything above this interface is transport-agnostic.
/// </summary>
public interface ISmcTransport : IDisposable
{
    /// <summary>Human-readable name of the underlying path, for diagnostics.</summary>
    string Name { get; }

    bool TryGetKeyInfo(string key, out SmcKeyInfo info);
    bool TryReadKey(string key, byte size, out byte[] data);
    bool TryWriteKey(string key, byte[] data);
}
