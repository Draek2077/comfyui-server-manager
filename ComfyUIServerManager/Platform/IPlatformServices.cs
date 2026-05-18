namespace ComfyUIServerManager.Platform;

/// <summary>
/// Platform-specific bits the rest of the app needs (process discovery for re-attach,
/// "launch at login" registration, default install paths to suggest).
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Look for an existing python process whose command line references the given
    /// main.py path. Returns the PID, or null if none found. Used to re-attach to a
    /// ComfyUI server that was already running before the manager started, or that
    /// was launched independently after a manager-initiated restart.
    /// </summary>
    int? FindRunningComfyProcess(string mainScriptPath);

    /// <summary>Register/unregister this app to launch at user login.</summary>
    void SetLaunchAtLogin(bool enabled, string executablePath);

    /// <summary>
    /// True if launch-at-login is currently configured for this app
    /// (registry value on Windows, autostart .desktop file on Linux).
    /// </summary>
    bool IsLaunchAtLoginEnabled();

    /// <summary>
    /// Human-readable label for the "launch at login" menu item and checkbox,
    /// since "Launch on Windows Start" reads weird on Linux.
    /// </summary>
    string LaunchAtLoginLabel { get; }
}

public static class PlatformServicesFactory
{
    public static IPlatformServices Create() =>
        OperatingSystem.IsWindows() ? new WindowsPlatformServices() : new LinuxPlatformServices();
}
