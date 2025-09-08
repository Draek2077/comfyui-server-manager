// (In the Services folder)

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComfyUIServerManagerModern.Models;

namespace ComfyUIServerManagerModern.Services;

public class ComfyUIProcessService : IProcessService
{
    private static readonly Regex ServerReadyRegex = new(@"^To see the GUI go to: http://", RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;
    private Process? _comfyUIProcess;
    private bool _stopExplicitlyRequested = false;

    public ServerState CurrentState { get; private set; } = ServerState.Stopped;
    public int? ProcessId => _comfyUIProcess?.Id;

    public event Action<ServerState>? StateChanged;
    public event Action<string>? LogReceived;

    public ComfyUIProcessService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Start()
    {
        if (CurrentState != ServerState.Stopped) return;
        
        var settings = _settingsService.CurrentSettings;
        if (string.IsNullOrEmpty(settings.ComfyUIPath) || !Directory.Exists(settings.ComfyUIPath))
        {
            Log("ComfyUI path is not set or does not exist.");
            return;
        }

        var pythonExe = Path.Combine(settings.ComfyUIPath, "python_embeded", "python.exe");
        var mainScript = Path.Combine(settings.ComfyUIPath, "main.py");
        
        if (!File.Exists(pythonExe) || !File.Exists(mainScript))
        {
            Log($"Could not find python.exe or main.py in: {settings.ComfyUIPath}");
            return;
        }

        SetState(ServerState.Starting);
        _stopExplicitlyRequested = false;
        
        var arguments = $"-s \"{mainScript}\" --windows-standalone-build {settings.Flags.BuildArgumentString()}";
        var startInfo = new ProcessStartInfo(pythonExe, arguments)
        {
            WorkingDirectory = settings.ComfyUIPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            _comfyUIProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _comfyUIProcess.OutputDataReceived += OnDataReceived;
            _comfyUIProcess.ErrorDataReceived += OnDataReceived;
            _comfyUIProcess.Exited += OnProcessExited;
            _comfyUIProcess.Start();
            _comfyUIProcess.BeginOutputReadLine();
            _comfyUIProcess.BeginErrorReadLine();
            Log("ComfyUI server process starting...");
        }
        catch (Exception ex)
        {
            Log($"Failed to start ComfyUI: {ex.Message}");
            SetState(ServerState.Stopped);
        }
    }

    public void Stop()
    {
        if (CurrentState == ServerState.Stopped || _comfyUIProcess == null) return;
        
        _stopExplicitlyRequested = true;
        Log("Stopping ComfyUI server process...");

        try
        {
            if (!_comfyUIProcess.HasExited)
            {
                _comfyUIProcess.Kill(true); 
                _comfyUIProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Log($"Error stopping ComfyUI: {ex.Message}");
        }
        finally
        {
            _comfyUIProcess = null;
            SetState(ServerState.Stopped);
            Log("Server stopped.");
        }
    }

    public async void Restart()
    {
        if (CurrentState == ServerState.Stopped)
        {
            Start();
            return;
        }
        
        if (_comfyUIProcess != null)
        {
            _comfyUIProcess.Exited += OnExitedForRestart;
            Stop();
        }
    }
    
    private void OnExitedForRestart(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            process.Exited -= OnExitedForRestart;
        }
        // Give it a moment before restarting
        Task.Delay(1000).ContinueWith(_ => Start());
    }

    private void OnDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        Log(e.Data);
        
        if (CurrentState == ServerState.Starting && ServerReadyRegex.IsMatch(e.Data))
        {
            SetState(ServerState.Running);
            Log("Server startup confirmed.");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Log("ComfyUI server process has exited.");
        _comfyUIProcess = null;
        SetState(ServerState.Stopped);

        if (!_stopExplicitlyRequested && _settingsService.CurrentSettings.AutoRestartOnCrash)
        {
            Log("Auto-restarting server in 5 seconds...");
            Task.Delay(5000).ContinueWith(_ =>
            {
                if (CurrentState == ServerState.Stopped)
                {
                    Start();
                }
            });
        }
    }

    private void SetState(ServerState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        StateChanged?.Invoke(newState);
    }
    
    private void Log(string message)
    {
        Debug.WriteLine(message);
        LogReceived?.Invoke(message);
    }

    public bool TryAttachToExistingProcess()
    {
        var settings = _settingsService.CurrentSettings;
        if (string.IsNullOrEmpty(settings.ComfyUIPath) || !Directory.Exists(settings.ComfyUIPath)) return false;

        var mainScript = Path.Combine(settings.ComfyUIPath, "main.py");
        if (!File.Exists(mainScript)) return false;

        try
        {
            var query = $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'python.exe' AND CommandLine LIKE '%{mainScript.Replace("\\", "\\\\")}%'";
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            var processObject = results.OfType<ManagementObject>().FirstOrDefault();

            if (processObject != null)
            {
                var pid = Convert.ToInt32(processObject["ProcessId"]);
                _comfyUIProcess = Process.GetProcessById(pid);
                _comfyUIProcess.EnableRaisingEvents = true;
                _comfyUIProcess.Exited += OnProcessExited;
                
                // Note: We can't redirect output of an already running process.
                // We lose live logging on attachment, but we gain state management.
                
                SetState(ServerState.Running);
                Log($"Attached to existing ComfyUI process (PID: {pid}).");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"Error during process scan: {ex.Message}");
        }

        return false;
    }
}