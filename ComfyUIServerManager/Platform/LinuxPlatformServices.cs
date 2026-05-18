using System.IO;

namespace ComfyUIServerManager.Platform;

// No [SupportedOSPlatform("linux")] — the class itself is pure file IO and
// compiles/runs on any OS; it simply finds no /proc entries when run elsewhere.
// Marking it Linux-only triggers CA1416 in the factory's else-branch.
public class LinuxPlatformServices : IPlatformServices
{
    private const string DesktopFileName = "comfyui-server-manager.desktop";

    public string LaunchAtLoginLabel => "Launch at Login";

    public int? FindRunningComfyProcess(string mainScriptPath)
    {
        // Walk /proc/*/cmdline looking for an argv that references main.py at the
        // expected path. /proc entries are NUL-separated argv strings; substring
        // match on the joined string is good enough (and matches what the Windows
        // WMI path does — a CommandLine substring check).
        try
        {
            foreach (var procDir in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(procDir);
                if (!int.TryParse(name, out var pid)) continue;

                var cmdlinePath = Path.Combine(procDir, "cmdline");
                if (!File.Exists(cmdlinePath)) continue;

                string cmdline;
                try
                {
                    cmdline = File.ReadAllText(cmdlinePath).Replace('\0', ' ');
                }
                catch
                {
                    // /proc/<pid>/cmdline read can race against process exit, and we
                    // can't read processes owned by other users. Skip and keep going.
                    continue;
                }

                if (cmdline.Contains(mainScriptPath) && cmdline.Contains("python"))
                    return pid;
            }
        }
        catch
        {
            // /proc unreadable (containerised sandbox etc) — just give up on re-attach.
        }

        return null;
    }

    public void SetLaunchAtLogin(bool enabled, string executablePath)
    {
        var autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart");
        var desktopFile = Path.Combine(autostartDir, DesktopFileName);

        if (!enabled)
        {
            if (File.Exists(desktopFile)) File.Delete(desktopFile);
            return;
        }

        Directory.CreateDirectory(autostartDir);

        // Prefer the installed launcher symlink over the bare publish-output
        // binary path; if we're running under /opt/comfyui-server-manager/...
        // then /usr/bin/comfyui-server-manager will exist and is the stable
        // launcher GNOME / KDE will start.
        var exec = File.Exists("/usr/bin/comfyui-server-manager")
            ? "/usr/bin/comfyui-server-manager"
            : executablePath;

        File.WriteAllText(desktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=ComfyUI Server Manager
            Comment=Tray tool to manage the ComfyUI server
            Exec={exec}
            Icon=comfyui-server-manager
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """);
    }

    public bool IsLaunchAtLoginEnabled()
    {
        var desktopFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart", DesktopFileName);
        return File.Exists(desktopFile);
    }
}
