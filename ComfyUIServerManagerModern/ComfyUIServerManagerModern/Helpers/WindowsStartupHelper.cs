using System;
using Microsoft.Win32;
using System.Reflection;

namespace ComfyUIServerManagerModern.Helpers;

public static class WindowsStartupHelper
{
    public static void SetStartup(bool shouldStartWithWindows, string appName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                var executablePath = Assembly.GetExecutingAssembly().Location;
                // For unpackaged apps, Location is the .exe. For packaged apps, it might be empty.
                // We need a robust way to get the path. For now, this is a simplification.
                if (string.IsNullOrEmpty(executablePath))
                {
                    // This is a more complex topic in packaged apps.
                    // A common workaround is to use a launcher executable.
                    // For this example, we'll assume it works or fail gracefully.
                    return;
                }

                if (shouldStartWithWindows)
                {
                    key.SetValue(appName, $"\"{executablePath}\"");
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not update startup settings: {ex.Message}");
        }
    }
}