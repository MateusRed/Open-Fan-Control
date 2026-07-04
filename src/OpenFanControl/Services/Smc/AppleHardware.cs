using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// Detects whether we're running on genuine Apple hardware by scanning the raw SMBIOS
/// firmware table for Apple's manufacturer string. This gate must pass before any SMC
/// port I/O happens, so we never poke ports 0x300/0x304 on a non-Mac PC.
/// </summary>
internal static class AppleHardware
{
    // 'RSMB' — raw SMBIOS firmware table provider.
    private const uint RawSmbiosProvider = 0x52534D42;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature,
                                                      uint firmwareTableId,
                                                      byte[]? buffer,
                                                      uint bufferSize);

    private static bool? _cached;

    public static bool IsApple()
    {
        _cached ??= DetectApple();
        return _cached.Value;
    }

    private static bool DetectApple()
    {
        try
        {
            uint size = GetSystemFirmwareTable(RawSmbiosProvider, 0, null, 0);
            if (size == 0) return false;

            var buffer = new byte[size];
            uint written = GetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, size);
            if (written == 0 || written > size) return false;

            var text = Encoding.ASCII.GetString(buffer, 0, (int)written);
            return text.Contains("Apple Inc.", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("Apple Computer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
