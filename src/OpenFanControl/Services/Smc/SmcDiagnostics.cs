using System;
using System.IO;

namespace OpenFanControl.Services.Smc;

/// <summary>Appends SMC discovery/protocol details to a log for troubleshooting fan detection.</summary>
internal static class SmcDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenFanControl", "smc-debug.log");

    public static void Reset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"=== SMC diagnostics {DateTime.Now} ==={Environment.NewLine}");
        }
        catch { /* ignore */ }
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
        catch { /* ignore */ }
    }
}
