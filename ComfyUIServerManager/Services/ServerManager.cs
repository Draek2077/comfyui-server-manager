using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ComfyUIServerManager.Models;
using ComfyUIServerManager.Platform;
using Timer = System.Timers.Timer;

namespace ComfyUIServerManager.Services;

public enum ServerState
{
    Stopped,
    Starting,
    Running
}

public class ServerStateChangedEventArgs(ServerState state, int? pid) : EventArgs
{
    public ServerState State { get; } = state;
    public int? Pid { get; } = pid;
}

public class LogLineEventArgs(string line) : EventArgs
{
    public string Line { get; } = line;
}

/// <summary>
/// Owns the ComfyUI child process lifecycle. UI components subscribe to State/LogLine
/// events and call Start/Stop/Restart; they never see the Process directly.
/// </summary>
public class ServerManager
{
    private static readonly Regex ServerReadyRegex = new(@"^To see the GUI go to: http://", RegexOptions.Compiled);

    private readonly IPlatformServices _platform;
    private readonly StringBuilder _logBuffer = new();
    private readonly object _bufferLock = new();

    private Process? _comfyProcess;
    private string? _lastLaunchedScriptPath;
    private bool _stopExplicitlyRequested;
    private int _reattachAttempts;
    private Timer? _reattachTimer;
    private Timer? _autoRestartDelayTimer;
    private Timer? _autoRestartTimer;

    public ServerManager(IPlatformServices platform)
    {
        _platform = platform;
    }

    public ServerState State { get; private set; } = ServerState.Stopped;
    public int? CurrentPid => _comfyProcess?.Id;
    public AppSettings Settings { get; set; } = new();

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<LogLineEventArgs>? LogLineReceived;

    public string GetLogSnapshot()
    {
        lock (_bufferLock) return _logBuffer.ToString();
    }

    /// <summary>
    /// Try to find and re-attach to a ComfyUI process that's already running. Called at
    /// startup so users who restart the manager don't have to also restart the server.
    /// </summary>
    public bool TryAttachToExistingProcess()
    {
        var (resolved, _) = LaunchResolver.Resolve(Settings);
        if (resolved == null) return false;

        _lastLaunchedScriptPath = resolved.MainScript;
        var pid = _platform.FindRunningComfyProcess(resolved.MainScript);
        if (pid is null) return false;

        try
        {
            var existing = Process.GetProcessById(pid.Value);
            existing.EnableRaisingEvents = true;
            existing.Exited += OnProcessExited;
            _comfyProcess = existing;
            // We can't recover stdout/stderr on a process we didn't spawn, but state +
            // PID are correct. Mark as Running (not Starting) — the GUI URL line is in
            // the past and we don't have a way to observe it now.
            SetState(ServerState.Running);
            Log($"Attached to existing ComfyUI process (PID: {pid.Value}). Log output from before this session is not available.");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        if (State != ServerState.Stopped) return;

        var (resolved, error) = LaunchResolver.Resolve(Settings);
        if (resolved == null)
        {
            Log("Cannot start ComfyUI:");
            Log("  " + (error ?? "unknown configuration error"));
            return;
        }

        SetState(ServerState.Starting);
        _lastLaunchedScriptPath = resolved.MainScript;

        var argsPrefix = resolved.IsWindowsStandalone
            ? $"-s \"{resolved.MainScript}\" --windows-standalone-build"
            : $"-s \"{resolved.MainScript}\"";
        var arguments = $"{argsPrefix} {Settings.Flags.BuildArgumentString()}".Trim();

        var psi = new ProcessStartInfo(resolved.PythonExecutable, arguments)
        {
            WorkingDirectory = resolved.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => Log(e.Data);
            p.ErrorDataReceived += (_, e) => Log(e.Data);
            p.Exited += OnProcessExited;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _comfyProcess = p;
            Log($"ComfyUI server process starting (PID: {p.Id})...");
            Log($"  python:  {resolved.PythonExecutable}");
            Log($"  main:    {resolved.MainScript}");
            Log($"  argv:    {arguments}");
        }
        catch (Exception ex)
        {
            Log($"Failed to start ComfyUI: {ex.Message}");
            _comfyProcess = null;
            SetState(ServerState.Stopped);
        }
    }

    public void Stop()
    {
        DisposeTimers();
        _stopExplicitlyRequested = true;
        if (State == ServerState.Stopped) return;

        try
        {
            Log("Stopping ComfyUI server process...");
            var wasAutoRestart = Settings.AutoRestartOnCrash;
            Settings.AutoRestartOnCrash = false;

            if (_comfyProcess is { HasExited: false } p)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }

            _comfyProcess = null;
            SetState(ServerState.Stopped);
            Settings.AutoRestartOnCrash = wasAutoRestart;
            Log("Server stopped.");
        }
        catch (Exception ex)
        {
            Log($"Error stopping ComfyUI: {ex.Message}");
        }
    }

    public void Restart()
    {
        if (State == ServerState.Running)
        {
            Log("Restarting ComfyUI server...");
            if (_comfyProcess != null) _comfyProcess.Exited += RestartAfterStop;
            Stop();
        }
        else if (State == ServerState.Stopped)
        {
            Log("Server was not running. Starting...");
            Start();
        }
    }

    private async void RestartAfterStop(object? sender, EventArgs e)
    {
        if (sender is Process p) p.Exited -= RestartAfterStop;
        await Task.Delay(1000);
        Start();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (State == ServerState.Stopped) return;

        _comfyProcess = null;
        SetState(ServerState.Stopped);
        Log("ComfyUI server process has exited.");

        if (_stopExplicitlyRequested)
        {
            _stopExplicitlyRequested = false;
            return;
        }

        // Unexpected exit — start the re-attach search (server may have restarted
        // itself via the front-end "restart" button) and, if auto-restart is enabled,
        // schedule a manager-initiated start after a grace period.
        InitiateReattachSearch();

        if (Settings.AutoRestartOnCrash)
        {
            _autoRestartDelayTimer = new Timer(15000) { AutoReset = false };
            _autoRestartDelayTimer.Elapsed += (_, _) =>
            {
                if (State != ServerState.Stopped) return;
                Log("Auto-restarting server in 5 seconds...");
                _autoRestartTimer = new Timer(5000) { AutoReset = false };
                _autoRestartTimer.Elapsed += (_, _) => Start();
                _autoRestartTimer.Start();
            };
            _autoRestartDelayTimer.Start();
        }
    }

    private void InitiateReattachSearch()
    {
        if (string.IsNullOrEmpty(_lastLaunchedScriptPath)) return;

        _reattachAttempts = 0;
        _reattachTimer = new Timer(500) { AutoReset = true };
        _reattachTimer.Elapsed += ReattachTick;
        _reattachTimer.Start();
        Log("Searching for orphaned ComfyUI process to re-attach...");
    }

    private void ReattachTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _reattachAttempts++;
        if (_reattachAttempts > 30) // ~15 seconds
        {
            _reattachTimer?.Stop();
            _reattachTimer?.Dispose();
            _reattachTimer = null;
            Log("Re-attach search timed out.");
            return;
        }

        var pid = _platform.FindRunningComfyProcess(_lastLaunchedScriptPath!);
        if (pid is null) return;

        try
        {
            _reattachTimer?.Stop();
            _reattachTimer?.Dispose();
            _reattachTimer = null;
            var p = Process.GetProcessById(pid.Value);
            p.EnableRaisingEvents = true;
            p.Exited += OnProcessExited;
            _comfyProcess = p;
            SetState(ServerState.Starting);
            Log($"Re-attached to new ComfyUI process (PID: {pid.Value}).");
        }
        catch (Exception ex)
        {
            Log($"Re-attach failed: {ex.Message}");
        }
    }

    private void Log(string? message)
    {
        if (message == null) return;

        if (State == ServerState.Starting && ServerReadyRegex.IsMatch(message))
        {
            SetState(ServerState.Running);
            // Recurse with the announcement now that state is Running.
            Log("Server startup confirmed.");
        }

        lock (_bufferLock) _logBuffer.AppendLine(message);
        LogLineReceived?.Invoke(this, new LogLineEventArgs(message));
    }

    private void SetState(ServerState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(newState, _comfyProcess?.Id));
    }

    private void DisposeTimers()
    {
        _reattachTimer?.Stop(); _reattachTimer?.Dispose(); _reattachTimer = null;
        _autoRestartDelayTimer?.Stop(); _autoRestartDelayTimer?.Dispose(); _autoRestartDelayTimer = null;
        _autoRestartTimer?.Stop(); _autoRestartTimer?.Dispose(); _autoRestartTimer = null;
    }
}
