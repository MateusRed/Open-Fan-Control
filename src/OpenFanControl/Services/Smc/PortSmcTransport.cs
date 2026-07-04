using System;
using System.Text;
using System.Threading;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// Talks to the Apple SMC through legacy I/O ports (0x300 data / 0x304 command), using the
/// same handshake as the Linux <c>applesmc</c> driver. This works on pre-T2 Intel Macs (and
/// PC-style SuperIO Macs); on T2 Macs the ports read back 0xFF and <see cref="DriverSmcTransport"/>
/// is used instead. Port access rides on LibreHardwareMonitor's WinRing0 driver via <see cref="PortIo"/>.
///
/// Only ever touch these ports on genuine Apple hardware — see <see cref="HardwareMonitorService"/>.
/// </summary>
public sealed class PortSmcTransport : ISmcTransport
{
    private const uint DataPort = 0x300;
    private const uint CommandPort = 0x304;

    private const byte CmdRead = 0x10;
    private const byte CmdWrite = 0x11;
    private const byte CmdReadKeyType = 0x13;

    private const byte StatusAwaitingData = 0x01; // bit 0
    private const byte StatusInputClosed = 0x02;  // bit 1
    private const byte StatusBusy = 0x04;         // bit 2

    private readonly object _lock = new();

    public string Name => "Legacy I/O ports (0x300)";

    // ---- Low-level handshake (mirrors applesmc.c) ----

    private static bool WaitStatus(byte value, byte mask)
    {
        for (int i = 0; i < 24; i++)
        {
            byte status = PortIo.Inb(CommandPort);
            if ((status & mask) == value)
                return true;

            // No usleep in managed code; a short spin is plenty — the SMC is normally
            // ready on the first read, and this only backs off under contention.
            Thread.SpinWait(100 << Math.Min(i, 10));
        }

        return false;
    }

    private static bool SendByte(byte b, uint port)
    {
        if (!WaitStatus(0, StatusInputClosed)) return false;
        if (!WaitStatus(StatusBusy, StatusBusy)) return false;
        PortIo.Outb(port, b);
        return true;
    }

    private static bool SendCommand(byte cmd)
    {
        if (!WaitStatus(0, StatusInputClosed)) return false;
        PortIo.Outb(CommandPort, cmd);
        return true;
    }

    private static bool SendArgument(string key)
    {
        if (key.Length != 4) return false;
        for (int i = 0; i < 4; i++)
            if (!SendByte((byte)key[i], DataPort))
                return false;
        return true;
    }

    private bool ReadInternal(byte cmd, string key, byte[] buffer, int len)
    {
        // Make sure the controller isn't mid-transaction.
        WaitStatus(0, StatusBusy);

        if (!SendCommand(cmd) || !SendArgument(key))
            return false;
        if (!SendByte((byte)len, DataPort))
            return false;

        for (int i = 0; i < len; i++)
        {
            if (!WaitStatus((byte)(StatusAwaitingData | StatusBusy),
                            (byte)(StatusAwaitingData | StatusBusy)))
                return false;
            buffer[i] = PortIo.Inb(DataPort);
        }

        return true;
    }

    private bool WriteInternal(byte cmd, string key, byte[] buffer, int len)
    {
        WaitStatus(0, StatusBusy);

        if (!SendCommand(cmd) || !SendArgument(key))
            return false;
        if (!SendByte((byte)len, DataPort))
            return false;

        for (int i = 0; i < len; i++)
            if (!SendByte(buffer[i], DataPort))
                return false;

        return WaitStatus(0, StatusBusy);
    }

    // ---- Key access ----

    public bool TryGetKeyInfo(string key, out SmcKeyInfo info)
    {
        info = default;
        var buffer = new byte[6];
        lock (_lock)
        {
            if (!ReadInternal(CmdReadKeyType, key, buffer, buffer.Length))
                return false;
        }

        // Layout: [0]=data size, [1..4]=type code, [5]=attributes.
        var type = Encoding.ASCII.GetString(buffer, 1, 4).TrimEnd('\0', ' ');
        info = new SmcKeyInfo(buffer[0], type);
        return info.DataSize is > 0 and <= 32;
    }

    public bool TryReadKey(string key, byte size, out byte[] data)
    {
        data = new byte[size];
        lock (_lock)
            return ReadInternal(CmdRead, key, data, size);
    }

    public bool TryWriteKey(string key, byte[] data)
    {
        lock (_lock)
            return WriteInternal(CmdWrite, key, data, data.Length);
    }

    public void Dispose() { /* PortIo is shared and owned by LibreHardwareMonitor. */ }

    // ---- Diagnostics (port-specific) ----

    /// <summary>Reads the command/status port a few times (for diagnostics).</summary>
    public static string SampleStatus()
    {
        var parts = new string[4];
        for (int i = 0; i < 4; i++)
            parts[i] = "0x" + PortIo.Inb(CommandPort).ToString("X2");
        return string.Join(" ", parts);
    }

    /// <summary>Dumps reference ports and the SMC I/O window to tell "driver broken" from "T2-mediated".</summary>
    public static string DumpPorts()
    {
        string R(uint p) => "0x" + PortIo.Inb(p).ToString("X2");
        var refPorts = $"ref: 0x60={R(0x60)} 0x64={R(0x64)} 0x61={R(0x61)}";
        var smc = "";
        for (uint p = 0x300; p <= 0x307; p++)
            smc += $" [{p:X3}]={R(p)}";
        return refPorts + " | smc:" + smc;
    }
}
