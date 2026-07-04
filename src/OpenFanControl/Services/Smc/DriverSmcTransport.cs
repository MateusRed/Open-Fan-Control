using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OpenFanControl.Services.Smc;

/// <summary>
/// Talks to the Apple SMC through a kernel driver that exposes it as the device
/// <c>\\.\APPLESMC</c>. This is the only way to reach the SMC on T2 Macs, where the SMC
/// interface lives in MMIO (the legacy 0x300 ports read back 0xFF) — the driver maps the
/// MMIO window reported by the ACPI <c>APP0001</c> device and runs the SMC command protocol
/// in kernel mode, exposing simple read/write-key IOCTLs to user space.
///
/// <para>The driver is the one shipped and registered by Macs Fan Control (service
/// <c>AppleSMC</c>, an EV-signed driver that loads even under HVCI). We do not install or
/// ship it — we only reuse it if it is already registered on the machine. If it isn't,
/// <see cref="TryCreate"/> returns null and the caller falls back to port I/O.</para>
///
/// <para>The IOCTL interface (METHOD_BUFFERED, device type 0x22) was determined from the
/// driver's own dispatch table:
/// <list type="bullet">
/// <item>0x220000 ReadKey — in: key[4]+len[1], out: data[len]</item>
/// <item>0x220004 WriteKey — in: key[4]+len[1]+data[len]</item>
/// <item>0x220008 KeyByIndex — in: index[4] big-endian, out: key[4]</item>
/// <item>0x22000C ReadKeyType — in: key[4], out: size[1]+type[4]+attr[1]</item>
/// <item>0x220020 Status — out: status[1]</item>
/// </list></para>
/// </summary>
public sealed class DriverSmcTransport : ISmcTransport
{
    private const string DevicePath = @"\\.\APPLESMC";
    private const string ServiceName = "AppleSMC";

    private const uint IOCTL_READ_KEY = 0x220000;
    private const uint IOCTL_WRITE_KEY = 0x220004;
    private const uint IOCTL_READ_KEY_TYPE = 0x22000C;

    private readonly SafeFileHandle _handle;
    private readonly object _lock = new();

    public string Name => @"AppleSMC kernel driver (\\.\APPLESMC)";

    private DriverSmcTransport(SafeFileHandle handle) => _handle = handle;

    /// <summary>
    /// Opens the driver, starting its service first if it's registered but stopped.
    /// Returns null if the driver isn't present or can't be opened.
    /// </summary>
    public static DriverSmcTransport? TryCreate()
    {
        var handle = OpenDevice();
        if (handle is null)
        {
            // Registered-but-stopped is the common case (the driver only runs while some
            // app holds it open). Try to start it, then open again.
            if (TryStartService())
                handle = OpenDevice();
        }

        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            return null;
        }

        return new DriverSmcTransport(handle);
    }

    private static SafeFileHandle? OpenDevice()
    {
        var h = CreateFile(DevicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero,
                           OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Dispose();
            return null;
        }
        return h;
    }

    // ---- Key access ----

    public bool TryGetKeyInfo(string key, out SmcKeyInfo info)
    {
        info = default;
        if (!TryKeyBytes(key, out var keyBytes)) return false;

        var outBuf = new byte[6];
        lock (_lock)
        {
            if (!Ioctl(IOCTL_READ_KEY_TYPE, keyBytes, outBuf, out uint returned) || returned < 6)
                return false;
        }

        // Layout: [0]=data size, [1..4]=type code, [5]=attributes.
        var type = Encoding.ASCII.GetString(outBuf, 1, 4).TrimEnd('\0', ' ');
        info = new SmcKeyInfo(outBuf[0], type);
        return info.DataSize is > 0 and <= 32;
    }

    // SMC keys are at most 32 bytes; the driver validates the ReadKey output buffer as
    // >= 0x20 regardless of how many bytes the key actually has, so always hand it 32.
    private const int MaxKeyBytes = 32;

    public bool TryReadKey(string key, byte size, out byte[] data)
    {
        data = new byte[size];
        if (size == 0 || size > MaxKeyBytes || !TryKeyBytes(key, out var keyBytes)) return false;

        // Input: key[4] + requested length[1]. Output must be >= 32 bytes (driver requirement).
        var inBuf = new byte[5];
        Array.Copy(keyBytes, inBuf, 4);
        inBuf[4] = size;
        var outBuf = new byte[MaxKeyBytes];

        lock (_lock)
        {
            if (!Ioctl(IOCTL_READ_KEY, inBuf, outBuf, out uint returned) || returned < size)
                return false;
        }

        Array.Copy(outBuf, data, size);
        return true;
    }

    public bool TryWriteKey(string key, byte[] data)
    {
        if (data is null || data.Length == 0 || data.Length > MaxKeyBytes) return false;
        if (!TryKeyBytes(key, out var keyBytes)) return false;

        // Input: key[4] + length[1] + data[length]. The driver also requires a non-empty
        // output buffer (>= 1 byte) even though WriteKey returns nothing.
        var inBuf = new byte[5 + data.Length];
        Array.Copy(keyBytes, inBuf, 4);
        inBuf[4] = (byte)data.Length;
        Array.Copy(data, 0, inBuf, 5, data.Length);
        var outBuf = new byte[1];

        lock (_lock)
            return Ioctl(IOCTL_WRITE_KEY, inBuf, outBuf, out _);
    }

    private static bool TryKeyBytes(string key, out byte[] bytes)
    {
        bytes = new byte[4];
        if (key is null || key.Length != 4) return false;
        for (int i = 0; i < 4; i++) bytes[i] = (byte)key[i];
        return true;
    }

    private bool Ioctl(uint code, byte[] inBuf, byte[] outBuf, out uint returned)
    {
        returned = 0;
        if (_handle.IsInvalid) return false;
        return DeviceIoControl(_handle, code,
            inBuf, (uint)inBuf.Length,
            outBuf, (uint)outBuf.Length,
            out returned, IntPtr.Zero);
    }

    public void Dispose() => _handle?.Dispose();

    // ---- Service management (start the registered-but-stopped driver) ----

    private static bool TryStartService()
    {
        IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
        try
        {
            scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;

            svc = OpenService(scm, ServiceName, SERVICE_START | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false; // not registered

            if (!StartService(svc, 0, null))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_SERVICE_ALREADY_RUNNING)
                    return false;
            }

            // Give the device a moment to appear.
            for (int i = 0; i < 20; i++)
            {
                using var probe = OpenDevice();
                if (probe is { IsInvalid: false }) return true;
                Thread.Sleep(25);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (svc != IntPtr.Zero) CloseServiceHandle(svc);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }

    // ---- P/Invoke ----

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
        uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        byte[] inBuffer, uint inBufferSize, byte[] outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, uint numArgs, string[]? args);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
