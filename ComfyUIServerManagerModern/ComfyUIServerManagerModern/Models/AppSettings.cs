// (In the Models folder)
using System.Text;

namespace ComfyUIServerManagerModern.Models;

public class ComfyUIFlags
{
    // Enums for mutually exclusive options
    public enum AttentionType { pytorch, split, quad, sage, flash }
    public enum LogLevel { NONE, DEBUG, INFO, WARNING, ERROR, CRITICAL }
    public enum ProcessingUnit { default_gpu, gpu_only, cpu }
    public enum VramPreset { normalvram, highvram, lowvram, novram }

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
                sb.Append($"--{VramMode.ToString()} ");
                if (CudaDevice != 0) sb.Append($"--cuda-device {CudaDevice} ");
                break;
            case ProcessingUnit.cpu:
                sb.Append("--cpu ");
                break;
            case ProcessingUnit.default_gpu:
            default:
                sb.Append($"--{VramMode.ToString()} ");
                if (CudaDevice != 0) sb.Append($"--cuda-device {CudaDevice} ");
                break;
        }
        if (DisableAllCustomNodes) sb.Append("--disable-all-custom-nodes ");
        if (VerboseLevel != LogLevel.NONE) sb.Append($"--verbose {VerboseLevel.ToString()} ");
        if (!string.IsNullOrWhiteSpace(OutputDirectory)) sb.Append($"--output-directory \"{OutputDirectory}\" ");
        if (!string.IsNullOrWhiteSpace(ExtraModelPathsConfig)) sb.Append($"--extra-model-paths-config \"{ExtraModelPathsConfig}\" ");
        if (UseLatestFrontEnd) sb.Append("--front-end-version Comfy-Org/ComfyUI_frontend@latest ");
        else if (!string.IsNullOrWhiteSpace(CustomFrontEndVersion)) sb.Append($"--front-end-version Comfy-Org/ComfyUI_frontend@{CustomFrontEndVersion} ");
        return sb.ToString().Trim();
    }
}

public class AppSettings
{
    public string ComfyUIPath { get; set; } = "";
    public bool AutoRestartOnCrash { get; set; }
    public bool LaunchOnWindowsStart { get; set; }
    public bool AutoStartServerOnLaunch { get; set; }
    public ComfyUIFlags Flags { get; set; } = new();
}