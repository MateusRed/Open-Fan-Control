using System;
using System.Threading;
using Avalonia;

namespace OpenFanControl;

internal static class Program
{
    private static Mutex? _singleInstance;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called:
    // things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Only one instance may drive the fans at a time.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\OpenFanControl.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Last-resort crash log so a hardware-driver failure doesn't vanish silently.
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OpenFanControl");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, "crash.log"),
                    DateTime.Now + Environment.NewLine + ex);
            }
            catch
            {
                // ignored
            }

            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
