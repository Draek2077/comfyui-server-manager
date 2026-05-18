using System;
using Avalonia;

namespace ComfyUIServerManager;

internal static class Program
{
    private const string AppMutexName = "ComfyUIServerManager-7E2B4E9A-3C1D-4B5F-8D9A-2C1B4E9A3C1D";

    [STAThread]
    public static void Main(string[] args)
    {
        // Single-instance gate. Named mutexes work cross-platform; on Linux they map
        // onto a /tmp/.dotnet/ file. If we can't take it, another manager is already
        // running — exit silently so the user just brings the existing tray menu up.
        using var mutex = new System.Threading.Mutex(initiallyOwned: true, AppMutexName, out var createdNew);
        if (!createdNew) return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { /* not owned -> nothing to release */ }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                // Match StartupWMClass in the .desktop file so GNOME/KDE associate
                // any windows we open with the installed icon.
                WmClass = "ComfyUIServerManager"
            })
            .WithInterFont()
            .LogToTrace();
}
