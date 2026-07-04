using System;
using System.Diagnostics;
using System.IO;

namespace OpenFanControl.Services;

/// <summary>
/// Manages "start with Windows". Because the app runs elevated, a plain HKCU\Run entry
/// would trigger a UAC prompt at every login. Instead we register a Task Scheduler
/// logon task with highest privileges, which starts silently.
/// </summary>
public sealed class AutostartService
{
    private const string TaskName = "OpenFanControl";

    /// <summary>Path to the real app executable, or null when running under the dev host (dotnet).</summary>
    private static string? AppExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return null;
            return string.Equals(Path.GetFileName(path), "OpenFanControl.exe", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }
    }

    /// <summary>False when we can't manage autostart (e.g. running via `dotnet App.dll`).</summary>
    public bool IsSupported => AppExecutablePath is not null;

    public bool IsEnabled() => RunSchtasks($"/query /tn \"{TaskName}\"") == 0;

    public void Apply(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }

    private void Enable()
    {
        var exe = AppExecutablePath;
        if (exe is null) return;

        // /rl highest → run elevated without a UAC prompt at logon.
        RunSchtasks($"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\"\" /sc onlogon /rl highest /f");
    }

    private void Disable() => RunSchtasks($"/delete /tn \"{TaskName}\" /f");

    private static int RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process is null) return -1;
            process.WaitForExit(5000);
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
