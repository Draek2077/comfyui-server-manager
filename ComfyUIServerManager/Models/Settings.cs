using System.IO;
using System.Text;
using System.Text.Json;

namespace ComfyUIServerManager.Models;

public class ComfyUIFlags
{
    public enum AttentionType
    {
        pytorch,
        split,
        quad,
        sage,
        flash
    }

    public enum LogLevel
    {
        NONE,
        DEBUG,
        INFO,
        WARNING,
        ERROR,
        CRITICAL
    }

    public enum ProcessingUnit
    {
        default_gpu,
        gpu_only,
        cpu
    }

    public enum VramPreset
    {
        normalvram,
        highvram,
        lowvram,
        novram
    }

    // General
    public int Port { get; set; } = 8188;
    public bool DisableAutoLaunch { get; set; }
    public bool DisableMetadata { get; set; }
    public bool MultiUser { get; set; }
    public bool DontPrintServer { get; set; }

    // Performance
    public AttentionType Attention { get; set; } = AttentionType.pytorch;
    public bool ForceFp16 { get; set; }
    public bool ForceFp32 { get; set; }
    public bool DisableXformers { get; set; }

    // Hardware
    public VramPreset VramMode { get; set; } = VramPreset.normalvram;
    public ProcessingUnit Processor { get; set; } = ProcessingUnit.default_gpu;
    public int CudaDevice { get; set; }

    // Advanced
    public bool DisableAllCustomNodes { get; set; }
    public LogLevel VerboseLevel { get; set; } = LogLevel.NONE;
    public string OutputDirectory { get; set; } = "";
    public string ExtraModelPathsConfig { get; set; } = "";
    public string CustomFrontEndVersion { get; set; } = "";
    public bool UseLatestFrontEnd { get; set; } = true;

    public string BuildArgumentString()
    {
        var sb = new StringBuilder();

        if (Port != 8188) sb.Append($"--port {Port} ");
        if (DisableAutoLaunch) sb.Append("--disable-auto-launch ");
        if (DisableMetadata) sb.Append("--disable-metadata ");
        if (MultiUser) sb.Append("--multi-user ");
        if (DontPrintServer) sb.Append("--dont-print-server ");

        if (ForceFp16) sb.Append("--force-fp16 ");
        if (ForceFp32) sb.Append("--force-fp32 ");
        if (DisableXformers) sb.Append("--disable-xformers ");

        switch (Attention)
        {
            case AttentionType.pytorch: sb.Append("--use-pytorch-cross-attention "); break;
            case AttentionType.split: sb.Append("--use-split-cross-attention "); break;
            case AttentionType.quad: sb.Append("--use-quad-cross-attention "); break;
            case AttentionType.sage: sb.Append("--use-sage-attention "); break;
            case AttentionType.flash: sb.Append("--use-flash-attention "); break;
        }

        switch (Processor)
        {
            case ProcessingUnit.gpu_only:
                sb.Append("--gpu-only ");
                sb.Append($"--{VramMode} ");
                if (CudaDevice != 0) sb.Append($"--cuda-device {CudaDevice} ");
                break;
            case ProcessingUnit.cpu:
                sb.Append("--cpu ");
                break;
            case ProcessingUnit.default_gpu:
            default:
                sb.Append($"--{VramMode} ");
                if (CudaDevice != 0) sb.Append($"--cuda-device {CudaDevice} ");
                break;
        }

        if (DisableAllCustomNodes) sb.Append("--disable-all-custom-nodes ");
        if (VerboseLevel != LogLevel.NONE) sb.Append($"--verbose {VerboseLevel} ");
        if (!string.IsNullOrWhiteSpace(OutputDirectory)) sb.Append($"--output-directory \"{OutputDirectory}\" ");
        if (!string.IsNullOrWhiteSpace(ExtraModelPathsConfig))
            sb.Append($"--extra-model-paths-config \"{ExtraModelPathsConfig}\" ");

        if (UseLatestFrontEnd)
            sb.Append("--front-end-version Comfy-Org/ComfyUI_frontend@latest ");
        else if (!string.IsNullOrWhiteSpace(CustomFrontEndVersion))
            sb.Append($"--front-end-version Comfy-Org/ComfyUI_frontend@{CustomFrontEndVersion} ");

        return sb.ToString().Trim();
    }
}

/// <summary>
/// How the manager should locate Python + ComfyUI's main.py for a given install.
/// </summary>
public enum LaunchMode
{
    /// <summary>
    /// Windows portable layout: &lt;path&gt;/python_embeded/python.exe + &lt;path&gt;/ComfyUI/main.py.
    /// This is what the Windows ComfyUI portable zip ships as.
    /// </summary>
    WindowsPortable,

    /// <summary>
    /// Linux/Mac venv layout: &lt;path&gt;/venv/bin/python + &lt;path&gt;/main.py
    /// (i.e. ComfyUIPath points at the ComfyUI clone itself, venv is a sibling subdir).
    /// </summary>
    Venv,

    /// <summary>
    /// Explicit paths — user picks the python executable and the main.py path independently.
    /// Useful for system Python, conda envs, or any non-standard layout.
    /// </summary>
    Custom
}

public class AppSettings
{
    /// <summary>
    /// Root path for the ComfyUI install. Interpreted per LaunchMode.
    /// </summary>
    public string ComfyUIPath { get; set; } = "";

    public LaunchMode LaunchMode { get; set; } = LaunchMode.WindowsPortable;

    /// <summary>Used only when LaunchMode = Custom.</summary>
    public string CustomPythonExecutable { get; set; } = "";

    /// <summary>Used only when LaunchMode = Custom.</summary>
    public string CustomMainScript { get; set; } = "";

    public bool AutoRestartOnCrash { get; set; }
    public bool LaunchOnSystemStart { get; set; }
    public bool AutoStartServerOnLaunch { get; set; }
    public ComfyUIFlags Flags { get; set; } = new();
}

public static class SettingsManager
{
    public const string AppName = "ComfyUIServerManager";

    public static readonly string AppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return DefaultsForPlatform();
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? DefaultsForPlatform();
        }
        catch
        {
            return DefaultsForPlatform();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataFolder);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, Options));
    }

    public static bool SettingsFileExists() => File.Exists(SettingsFilePath);

    private static AppSettings DefaultsForPlatform() => new()
    {
        LaunchMode = OperatingSystem.IsWindows() ? LaunchMode.WindowsPortable : LaunchMode.Venv
    };
}

/// <summary>
/// Resolves an AppSettings into a concrete (python-executable, main.py, working-dir) triple
/// the ServerManager can launch. Centralised so the launch logic doesn't have to know
/// about per-platform layouts.
/// </summary>
public static class LaunchResolver
{
    public record Resolved(string PythonExecutable, string MainScript, string WorkingDirectory, bool IsWindowsStandalone);

    /// <summary>
    /// Resolve settings into a launchable triple. On failure returns (null, message) where
    /// the message lists the paths we tried, so the user can see why detection failed.
    /// </summary>
    public static (Resolved? resolved, string? error) Resolve(AppSettings settings)
    {
        switch (settings.LaunchMode)
        {
            case LaunchMode.WindowsPortable:
            {
                if (string.IsNullOrWhiteSpace(settings.ComfyUIPath))
                    return (null, "Windows Portable: ComfyUI Path is empty. Open Settings and pick the folder that contains both python_embeded/ and ComfyUI/.");
                if (!Directory.Exists(settings.ComfyUIPath))
                    return (null, $"Windows Portable: folder does not exist: {settings.ComfyUIPath}");
                var py = Path.Combine(settings.ComfyUIPath, "python_embeded", "python.exe");
                var main = Path.Combine(settings.ComfyUIPath, "ComfyUI", "main.py");
                if (!File.Exists(py)) return (null, $"Windows Portable: python.exe not found at: {py}");
                if (!File.Exists(main)) return (null, $"Windows Portable: main.py not found at: {main}");
                return (new Resolved(py, main, settings.ComfyUIPath, IsWindowsStandalone: true), null);
            }
            case LaunchMode.Venv:
            {
                if (string.IsNullOrWhiteSpace(settings.ComfyUIPath))
                    return (null, "Venv: ComfyUI Path is empty. Pick either the ComfyUI clone (contains main.py) or its parent folder containing ComfyUI/ and venv/.");
                if (!Directory.Exists(settings.ComfyUIPath))
                    return (null, $"Venv: folder does not exist: {settings.ComfyUIPath}");

                // Two layouts we accept:
                //   A: <path>/main.py    + <path>/venv/{bin/python|Scripts/python.exe}
                //      (user picked the ComfyUI clone itself, venv lives inside it)
                //   B: <path>/ComfyUI/main.py + <path>/venv/{bin/python|Scripts/python.exe}
                //      (user picked a workspace dir; ComfyUI/ and venv/ are siblings)
                var venvPy = OperatingSystem.IsWindows()
                    ? Path.Combine(settings.ComfyUIPath, "venv", "Scripts", "python.exe")
                    : Path.Combine(settings.ComfyUIPath, "venv", "bin", "python");

                var mainA = Path.Combine(settings.ComfyUIPath, "main.py");
                var mainB = Path.Combine(settings.ComfyUIPath, "ComfyUI", "main.py");

                string main;
                string workingDir;
                if (File.Exists(mainA))      { main = mainA; workingDir = settings.ComfyUIPath; }
                else if (File.Exists(mainB)) { main = mainB; workingDir = Path.GetDirectoryName(mainB)!; }
                else return (null,
                    $"Venv: main.py not found. Tried:\n  {mainA}\n  {mainB}\n"
                    + "Pick either the ComfyUI clone (contains main.py) or its parent folder containing a ComfyUI/ subfolder.");

                if (!File.Exists(venvPy))
                    return (null,
                        $"Venv: python executable not found at:\n  {venvPy}\n"
                        + "Expected a venv/ subfolder alongside main.py (or alongside the ComfyUI/ subfolder).");

                return (new Resolved(venvPy, main, workingDir, IsWindowsStandalone: false), null);
            }
            case LaunchMode.Custom:
            {
                if (string.IsNullOrWhiteSpace(settings.CustomPythonExecutable))
                    return (null, "Custom: Python executable path is empty.");
                if (!File.Exists(settings.CustomPythonExecutable))
                    return (null, $"Custom: python executable not found at: {settings.CustomPythonExecutable}");
                if (string.IsNullOrWhiteSpace(settings.CustomMainScript))
                    return (null, "Custom: main.py path is empty.");
                if (!File.Exists(settings.CustomMainScript))
                    return (null, $"Custom: main.py not found at: {settings.CustomMainScript}");
                var wd = Path.GetDirectoryName(settings.CustomMainScript) ?? settings.ComfyUIPath;
                return (new Resolved(settings.CustomPythonExecutable, settings.CustomMainScript, wd, IsWindowsStandalone: false), null);
            }
            default:
                return (null, $"Unknown launch mode: {settings.LaunchMode}");
        }
    }
}
