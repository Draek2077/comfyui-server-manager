using System.Diagnostics;
using System.Runtime.Versioning;

namespace ComfyUIServerManager.Platform;

[SupportedOSPlatform("windows")]
public class WindowsPlatformServices : IPlatformServices
{
    private const string AppName = "ComfyUI Server Manager";

    public string LaunchAtLoginLabel => "Launch on Windows Start";

    public int? FindRunningComfyProcess(string mainScriptPath)
    {
        // The old WinForms version used WMI (System.Management) for this. We don't
        // want to pull System.Management into the cross-platform build, so we fall
        // back to enumerating python processes and reading their command line via
        // `wmic` if available, or simply trust Process.MainModule.FileName +
        // (best-effort) the GetCommandLine fallback below.
        //
        // For Windows-portable installs the python executable lives under
        // <ComfyUIPath>\python_embeded\python.exe, so MainModule.FileName alone
        // is enough to identify the right process most of the time.
        try
        {
            foreach (var proc in Process.GetProcessesByName("python"))
            {
                try
                {
                    var modulePath = proc.MainModule?.FileName ?? "";
                    if (TryReadCommandLine(proc.Id, out var cmdLine) && cmdLine.Contains(mainScriptPath))
                        return proc.Id;
                    // python_embeded fallback: if the python binary path matches the
                    // expected portable layout, we'll re-attach without verifying argv.
                    if (modulePath.Contains("python_embeded", StringComparison.OrdinalIgnoreCase))
                        return proc.Id;
                }
                catch
                {
                    // Inaccessible (32 vs 64 bit, permissions etc) — skip.
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // ignore — re-attach is best effort
        }

        return null;
    }

    public void SetLaunchAtLogin(bool enabled, string executablePath)
    {
        // Use reflection-free conditional; ALL Microsoft.Win32 access stays in
        // this Windows-only file so the Linux build doesn't drag it in.
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;

        if (enabled) key.SetValue(AppName, $"\"{executablePath}\"");
        else key.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public bool IsLaunchAtLoginEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
        return key?.GetValue(AppName) != null;
    }

    private static bool TryReadCommandLine(int pid, out string commandLine)
    {
        // We avoid a hard System.Management dependency by spawning `wmic`. wmic is
        // deprecated on Windows 11 24H2+, but Process.MainModule check above will
        // catch the python_embeded case there; this is the belt to that suspenders.
        commandLine = "";
        try
        {
            var psi = new ProcessStartInfo("wmic",
                $"process where ProcessId={pid} get CommandLine /format:list")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            var idx = output.IndexOf("CommandLine=", StringComparison.Ordinal);
            if (idx < 0) return false;
            commandLine = output[(idx + "CommandLine=".Length)..].Trim();
            return commandLine.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
