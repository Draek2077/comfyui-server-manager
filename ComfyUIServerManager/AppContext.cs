using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Timers;
using Microsoft.Win32;
using MethodInvoker = System.Windows.Forms.MethodInvoker;
using Timer = System.Timers.Timer;

namespace ComfyUIServerManager;

#region Settings Classes

/// <summary>
/// Holds all the configurable command-line flags for ComfyUI.
/// </summary>
public class ComfyUIFlags
{
    // Enums for mutually exclusive options
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


    /// <summary>
    /// Builds the command-line argument string from the current settings.
    /// </summary>
    public string BuildArgumentString()
    {
        var sb = new StringBuilder();

        // General
        if (Port != 8188) sb.Append($"--port {Port} ");
        if (DisableAutoLaunch) sb.Append("--disable-auto-launch ");
        if (DisableMetadata) sb.Append("--disable-metadata ");
        if (MultiUser) sb.Append("--multi-user ");
        if (DontPrintServer) sb.Append("--dont-print-server ");

        // Performance
        if (ForceFp16) sb.Append("--force-fp16 ");
        if (ForceFp32) sb.Append("--force-fp32 ");
        if (DisableXformers) sb.Append("--disable-xformers ");

        // Attention Type
        switch (Attention)
        {
            case AttentionType.pytorch: sb.Append("--use-pytorch-cross-attention "); break;
            case AttentionType.split: sb.Append("--use-split-cross-attention "); break;
            case AttentionType.quad: sb.Append("--use-quad-cross-attention "); break;
            case AttentionType.sage: sb.Append("--use-sage-attention "); break;
            case AttentionType.flash: sb.Append("--use-flash-attention "); break;
        }

        // Hardware & VRAM
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

        // Advanced
        if (DisableAllCustomNodes) sb.Append("--disable-all-custom-nodes ");
        if (VerboseLevel != LogLevel.NONE) sb.Append($"--verbose {VerboseLevel.ToString()} ");
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
/// Main application settings class.
/// </summary>
public class AppSettings
{
    public string ComfyUIPath { get; set; } = "";
    public bool AutoRestartOnCrash { get; set; }
    public bool LaunchOnWindowsStart { get; set; }
    public bool AutoStartServerOnLaunch { get; set; }
    public ComfyUIFlags Flags { get; set; } = new();
}

#endregion

public class AppContext : ApplicationContext
{
    private static readonly string AppName = "ComfyUI Server Manager";
    private static readonly Regex ServerReadyRegex = new(@"^To see the GUI go to: http://", RegexOptions.Compiled);
    private readonly string? _comfyScript = "main.py";
    private readonly StringBuilder _logBuffer = new();
    private readonly string? _pythonCommand = "python_embeded\\python.exe";
    private readonly string _settingsPath;
    private Process? _comfyUIProcess;
    private Icon? _defaultIcon;
    private Icon? _iconOff;
    private Icon? _iconOn;

    // --- PROCESS RE-ATTACHMENT ---
    private string? _lastLaunchedScriptPath;
    private LogForm? _logForm;
    private int _reattachAttempts;

    private Timer? _reattachTimer; // Use System.Timers.Timer for background threads
    private ServerState _serverState = ServerState.Stopped;
    private AppSettings _settings = new();

    private bool _stopExplicitlyRequested;
    private NotifyIcon _trayIcon = new();

    public AppContext()
    {
        _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName,
            "settings.json");
        Application.ApplicationExit += OnApplicationExit;
        LoadIcons();
        InitializeComponent();

        var isFirstRun = !File.Exists(_settingsPath);
        LoadSettings();

        if (isFirstRun || string.IsNullOrEmpty(_settings.ComfyUIPath))
        {
            MessageBox.Show("Welcome! Please select your ComfyUI portable folder to get started.", "First-Time Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenSettings(null, new EventArgs());
        }

        var attached = TryAttachToExistingProcess();
        UpdateMenuState();

        if (!attached && _settings.AutoStartServerOnLaunch) StartComfyUI(null, new EventArgs());
    }

    private void LoadIcons()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("ComfyUIServerManager.Comfy_On.ico"))
            {
                if (stream != null) _iconOn = new Icon(stream);
            }

            using (var stream = assembly.GetManifestResourceStream("ComfyUIServerManager.Comfy_Off.ico"))
            {
                if (stream != null) _iconOff = new Icon(stream);
            }

            _defaultIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            if (_iconOn == null || _iconOff == null)
                LogToServer("Warning: Could not load custom status icons. Falling back to default.");
        }
        catch (Exception ex)
        {
            LogToServer($"Error loading icons: {ex.Message}");
            _defaultIcon = SystemIcons.Application; // A final fallback
        }
    }

    private void InitializeComponent()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Start ComfyUI Server", null, StartComfyUI) { Name = "start" },
            new ToolStripMenuItem("Restart ComfyUI Server", null, RestartComfyUI) { Name = "restart" },
            new ToolStripMenuItem("Stop ComfyUI Server", null, StopComfyUI) { Name = "stop" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("View Server Logs", null, ShowHideLogs) { Name = "logs" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Auto-Start Server on Launch", null, ToggleAutoStartServer) { Name = "autostart" },
            new ToolStripMenuItem("Auto-Restart Server on Crash", null, ToggleAutoRestart) { Name = "autorestart" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Launch on Windows Start", null, ToggleLaunchOnStart) { Name = "winstart" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Settings...", null, OpenSettings),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, Exit)
        });

        _trayIcon = new NotifyIcon
        {
            Icon = _iconOff ?? _defaultIcon,
            ContextMenuStrip = contextMenu,
            Visible = true,
            Text = AppName
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;
        _trayIcon.DoubleClick += ShowHideLogs;
    }

    private void UpdateMenuState()
    {
        var menu = _trayIcon.ContextMenuStrip;
        if (menu == null) return;

        var isRunning = _serverState == ServerState.Running;
        var isStarting = _serverState == ServerState.Starting;
        var isStopped = _serverState == ServerState.Stopped;

        menu.Items["start"]!.Enabled = isStopped;
        menu.Items["restart"]!.Enabled = isRunning;
        menu.Items["stop"]!.Enabled = isRunning || isStarting;
        menu.Items["logs"]!.Enabled = true;
        ((ToolStripMenuItem)menu.Items["logs"]!).Checked = _logForm != null && _logForm.Visible;
        ((ToolStripMenuItem)menu.Items["autostart"]!).Checked = _settings.AutoStartServerOnLaunch;
        ((ToolStripMenuItem)menu.Items["autorestart"]!).Checked = _settings.AutoRestartOnCrash;
        ((ToolStripMenuItem)menu.Items["winstart"]!).Checked = _settings.LaunchOnWindowsStart;

        switch (_serverState)
        {
            case ServerState.Running:
                _trayIcon.Text = $"{AppName} (Running - PID: {_comfyUIProcess?.Id ?? 0})";
                _trayIcon.Icon = _iconOn ?? _defaultIcon;
                break;
            case ServerState.Starting:
                _trayIcon.Text = $"{AppName} (Starting...)";
                _trayIcon.Icon = _defaultIcon; // "Pending" icon
                break;
            case ServerState.Stopped:
            default:
                _trayIcon.Text = $"{AppName} (Stopped)";
                _trayIcon.Icon = _iconOff ?? _defaultIcon;
                break;
        }
    }

    // --- STATE MANAGEMENT ---
    private enum ServerState
    {
        Stopped,
        Starting,
        Running
    }

    #region Event Handlers

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            if (_logForm != null && _logForm.Visible)
            {
                if (_logForm.WindowState == FormWindowState.Minimized) _logForm.WindowState = FormWindowState.Normal;
                _logForm.Activate();
            }
    }

    private void StartComfyUI(object? sender, EventArgs e)
    {
        if (_serverState != ServerState.Stopped) return;
        if (string.IsNullOrEmpty(_settings.ComfyUIPath) || !Directory.Exists(_settings.ComfyUIPath))
        {
            MessageBox.Show("ComfyUI path is not set. Please configure it in Settings.", "Config Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var pythonExe = Path.Combine(_settings.ComfyUIPath, "python_embeded", "python.exe");
        var mainScript = Path.Combine(_settings.ComfyUIPath, "ComfyUI", "main.py");
        if (!File.Exists(pythonExe) || !File.Exists(mainScript))
        {
            MessageBox.Show($"Could not find python.exe or main.py in:\n{_settings.ComfyUIPath}", "File Not Found",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _serverState = ServerState.Starting;
        UpdateMenuState();

        _lastLaunchedScriptPath = mainScript; // Store unique script path for re-attachment
        var arguments = $"-s \"{mainScript}\" --windows-standalone-build {_settings.Flags.BuildArgumentString()}";

        var startInfo = new ProcessStartInfo(pythonExe, arguments)
        {
            WorkingDirectory = _settings.ComfyUIPath,
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
            _comfyUIProcess.OutputDataReceived += (s, args) => LogToServer(args.Data);
            _comfyUIProcess.ErrorDataReceived += (s, args) => LogToServer(args.Data);
            _comfyUIProcess.Exited += OnComfyUIProcessExited;
            _comfyUIProcess.Start();
            _comfyUIProcess.BeginOutputReadLine();
            _comfyUIProcess.BeginErrorReadLine();
            LogToServer("ComfyUI server process starting...");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start ComfyUI:\n{ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _comfyUIProcess = null;
            _serverState = ServerState.Stopped;
            UpdateMenuState();
        }
    }

    private void StopComfyUI(object? sender, EventArgs e)
    {
        _reattachTimer?.Stop();
        _reattachTimer?.Dispose();
        _reattachTimer = null;

        _stopExplicitlyRequested = true;

        if (_serverState == ServerState.Stopped) return;

        try
        {
            LogToServer("Stopping ComfyUI server process...");
            var wasAutoRestarting = _settings.AutoRestartOnCrash;
            _settings.AutoRestartOnCrash = false;

            if (_comfyUIProcess != null && !_comfyUIProcess.HasExited)
            {
                _comfyUIProcess.Kill(true);
                _comfyUIProcess.WaitForExit(5000);
            }


            _serverState = ServerState.Stopped;
            _comfyUIProcess = null;
            _settings.AutoRestartOnCrash = wasAutoRestarting;
            LogToServer("Server stopped.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error stopping ComfyUI:\n{ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateMenuState();
        }
    }

    private void RestartComfyUI(object? sender, EventArgs e)
    {
        if (_serverState == ServerState.Running)
        {
            LogToServer("Restarting ComfyUI server...");
            if (_comfyUIProcess != null) _comfyUIProcess.Exited += StartAfterStop;


            StopComfyUI(sender, e);
        }
        else if (_serverState == ServerState.Stopped)
        {
            LogToServer("Server was not running. Starting...");
            StartComfyUI(sender, e);
        }
    }

    private async void StartAfterStop(object? sender, EventArgs e)
    {
        if (sender is Process process) process.Exited -= StartAfterStop;

        await Task.Delay(1000);

        if (_trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.InvokeRequired)
            _trayIcon.ContextMenuStrip.Invoke(new MethodInvoker(() => StartComfyUI(null, EventArgs.Empty)));
        else
            StartComfyUI(null, EventArgs.Empty);
    }

    private void OnComfyUIProcessExited(object? sender, EventArgs e)
    {
        if (_serverState == ServerState.Stopped)
            return;

        _comfyUIProcess = null;
        _serverState = ServerState.Stopped;
        LogToServer("ComfyUI server process has exited.");

        if (_trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.InvokeRequired)
            _trayIcon.ContextMenuStrip.Invoke(new MethodInvoker(UpdateMenuState));
        else
            UpdateMenuState();

        if (_stopExplicitlyRequested)
            _stopExplicitlyRequested = false;
        else
            InitiateReattachSearch();

        if (_settings.AutoRestartOnCrash)
        {
            var restartAction = () =>
            {
                if (_serverState == ServerState.Stopped)
                {
                    LogToServer("Auto-restarting server in 5 seconds...");
                    var restartTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                    restartTimer.Tick += (s, args) =>
                    {
                        StartComfyUI(null, new EventArgs());
                        restartTimer.Stop();
                        restartTimer.Dispose();
                    };
                    restartTimer.Start();
                }
            };

            var delayTimer = new System.Windows.Forms.Timer { Interval = 15000 }; // Wait 15s for re-attach to succeed
            delayTimer.Tick += (s, args) =>
            {
                restartAction();
                delayTimer.Stop();
                delayTimer.Dispose();
            };
            delayTimer.Start();
        }
    }

    private void ShowHideLogs(object? sender, EventArgs e)
    {
        if (_logForm == null || _logForm.IsDisposed)
        {
            _logForm = new LogForm();
            _logForm.SetInitialLogContent(_logBuffer.ToString());
        }

        if (_logForm.Visible)
        {
            _logForm.Hide();
        }
        else
        {
            _logForm.Show();
            _logForm.Activate();
        }

        UpdateMenuState();
    }

    private void LogToServer(string? message)
    {
        if (message == null) return;

        if (_serverState == ServerState.Starting && ServerReadyRegex.IsMatch(message))
        {
            _serverState = ServerState.Running;
            LogToServer("Server startup confirmed. UI state updated.");

            if (_trayIcon.ContextMenuStrip?.InvokeRequired ?? false)
                _trayIcon.ContextMenuStrip.Invoke(new MethodInvoker(UpdateMenuState));
            else
                UpdateMenuState();
        }

        _logBuffer.AppendLine(message);
        if (_logForm != null && !_logForm.IsDisposed && _logForm.Visible) _logForm.AppendLog(message);
    }

    private void ToggleAutoStartServer(object? sender, EventArgs e)
    {
        _settings.AutoStartServerOnLaunch = !_settings.AutoStartServerOnLaunch;
        UpdateMenuState();
        SaveSettings();
    }

    private void ToggleAutoRestart(object? sender, EventArgs e)
    {
        _settings.AutoRestartOnCrash = !_settings.AutoRestartOnCrash;
        UpdateMenuState();
        SaveSettings();
    }

    private void ToggleLaunchOnStart(object? sender, EventArgs e)
    {
        _settings.LaunchOnWindowsStart = !_settings.LaunchOnWindowsStart;
        UpdateRegistryForStartup(_settings.LaunchOnWindowsStart);
        UpdateMenuState();
        SaveSettings();
    }

    private void OpenSettings(object? sender, EventArgs e)
    {
        using (var settingsForm = new SettingsForm(_settings))
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                _settings = settingsForm.Settings;
                SaveSettings();
            }
        }
    }

    private void Exit(object? sender, EventArgs e)
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        StopComfyUI(null, EventArgs.Empty); // Ensure controlled shutdown
        Application.Exit();
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        _logForm?.Dispose();
        _trayIcon?.Dispose();
    }

    #endregion

    #region Process Re-attachment

    private bool TryAttachToExistingProcess()
    {
        if (string.IsNullOrEmpty(_settings.ComfyUIPath) || !Directory.Exists(_settings.ComfyUIPath)) return false;

        _lastLaunchedScriptPath = Path.Combine(_settings.ComfyUIPath, "ComfyUI", "main.py");
        if (!File.Exists(_lastLaunchedScriptPath)) return false;

        try
        {
            var query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'python.exe'";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject process in results)
                {
                    var commandLine = process["CommandLine"]?.ToString();

                    if (commandLine != null && commandLine.Contains(_pythonCommand) &&
                        commandLine.Contains(_comfyScript))
                    {
                        var pid = Convert.ToInt32(process["ProcessId"]);
                        _comfyUIProcess = Process.GetProcessById(pid);
                        _comfyUIProcess.EnableRaisingEvents = true;
                        _comfyUIProcess.Exited += OnComfyUIProcessExited;
                        _comfyUIProcess.OutputDataReceived += (s, args) => LogToServer(args.Data);
                        _comfyUIProcess.ErrorDataReceived += (s, args) => LogToServer(args.Data);
                        _serverState = ServerState.Running;
                        LogToServer($"Attached to existing ComfyUI process (PID: {pid}).");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogToServer($"Error during initial process scan: {ex.Message}");
        }

        return false;
    }

    private void InitiateReattachSearch()
    {
        if (string.IsNullOrEmpty(_lastLaunchedScriptPath)) return;

        _reattachAttempts = 0;
        _reattachTimer = new Timer(500); // Check every second
        _reattachTimer.Elapsed += ReattachTimer_Tick;
        _reattachTimer.AutoReset = true;
        _reattachTimer.Start();
        LogToServer("Searching for orphaned ComfyUI process to re-attach...");
    }

    private void ReattachTimer_Tick(object? sender, ElapsedEventArgs e)
    {
        _reattachAttempts++;
        if (_reattachAttempts > 5) // Timeout after 15 seconds
        {
            _reattachTimer?.Stop();
            _trayIcon.ContextMenuStrip?.Invoke(new MethodInvoker(() =>
            {
                LogToServer("Re-attach search timed out. Could not find new ComfyUI process.");
                _reattachTimer?.Dispose();
                _reattachTimer = null;
            }));
            return;
        }

        try
        {
            var query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'python.exe'";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject process in results)
                {
                    var commandLine = process["CommandLine"]?.ToString();
                    if (commandLine != null && commandLine.Contains(_lastLaunchedScriptPath!))
                    {
                        _reattachTimer?.Stop();
                        var pid = Convert.ToInt32(process["ProcessId"]);

                        _trayIcon.ContextMenuStrip?.Invoke(new MethodInvoker(() =>
                        {
                            _comfyUIProcess = Process.GetProcessById(pid);
                            _comfyUIProcess.EnableRaisingEvents = true;
                            _comfyUIProcess.Exited += OnComfyUIProcessExited;
                            _serverState = ServerState.Starting;
                            UpdateMenuState();
                            LogToServer($"Successfully re-attached to new process (PID: {pid}).");
                            _reattachTimer?.Dispose();
                            _reattachTimer = null;
                        }));
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _trayIcon.ContextMenuStrip?.Invoke(new MethodInvoker(() =>
            {
                LogToServer($"Error during re-attach search: {ex.Message}");
            }));
        }
    }

    #endregion

    #region Settings Management

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                _settings = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading settings:\n{ex.Message}\nDefaults will be used.", "Settings Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _settings = new AppSettings();
        }
        finally
        {
            UpdateRegistryForStartup(_settings.LaunchOnWindowsStart);
        }
    }

    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, options));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings:\n{ex.Message}", "Settings Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateRegistryForStartup(bool shouldStartWithWindows)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key != null)
                {
                    if (shouldStartWithWindows) key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
                    else key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update startup settings:\n{ex.Message}", "Registry Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    #endregion
}

internal static class Program
{
    private const string AppMutexName = "ComfyUIServerManager-7E2B4E9A-3C1D-4B5F-8D9A-2C1B4E9A3C1D";
    private static Mutex? appMutex;

    [STAThread]
    private static void Main()
    {
        appMutex = new Mutex(true, AppMutexName, out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show("ComfyUI Server Manager is already running.", "Application Already Running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new AppContext());
        }
        finally
        {
            appMutex?.ReleaseMutex();
            appMutex?.Dispose();
        }
    }
}

#region UI Forms

public class SettingsForm : Form
{
    private CheckBox chkDisableAutoLaunch = default!,
        chkDisableMetadata = default!,
        chkMultiUser = default!,
        chkDontPrintServer = default!,
        chkDisableCustomNodes = default!,
        chkFrontEndVersionLatest = default!;

    private CheckBox chkForceFp16 = default!, chkForceFp32 = default!, chkDisableXformers = default!;
    private CheckBox chkManagerAutoStart = default!, chkManagerAutoRestart = default!, chkManagerLaunchOnWin = default!;
    private ComboBox cmbVerboseLevel = default!;
    private NumericUpDown numPort = default!, numCudaDevice = default!;

    private RadioButton rbAttnPytorch = default!,
        rbAttnSplit = default!,
        rbAttnQuad = default!,
        rbAttnSage = default!,
        rbAttnFlash = default!;

    private RadioButton rbProcDefault = default!, rbProcGpuOnly = default!, rbProcCpu = default!;
    private RadioButton rbVramNormal = default!, rbVramHigh = default!, rbVramLow = default!, rbVramNo = default!;

    // Controls
    private TextBox txtComfyUIPath = default!,
        txtOutputDir = default!,
        txtExtraModelsPath = default!,
        txtFrontEndVersion = default!;

    public SettingsForm(AppSettings currentSettings)
    {
        // The JsonSerializer can return null, so we provide a fallback.
        Settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(currentSettings)) ??
                   new AppSettings();
        InitializeComponent();
        PopulateControls();
    }

    public AppSettings Settings { get; }

    private void PopulateControls()
    {
        // Manager
        txtComfyUIPath.Text = Settings.ComfyUIPath;
        chkManagerAutoStart.Checked = Settings.AutoStartServerOnLaunch;
        chkManagerAutoRestart.Checked = Settings.AutoRestartOnCrash;
        chkManagerLaunchOnWin.Checked = Settings.LaunchOnWindowsStart;

        // General
        numPort.Value = Settings.Flags.Port;
        chkDisableAutoLaunch.Checked = Settings.Flags.DisableAutoLaunch;
        chkDisableMetadata.Checked = Settings.Flags.DisableMetadata;
        chkMultiUser.Checked = Settings.Flags.MultiUser;
        chkDontPrintServer.Checked = Settings.Flags.DontPrintServer;

        // Performance
        chkForceFp16.Checked = Settings.Flags.ForceFp16;
        chkForceFp32.Checked = Settings.Flags.ForceFp32;
        chkDisableXformers.Checked = Settings.Flags.DisableXformers;
        rbAttnPytorch.Checked = Settings.Flags.Attention == ComfyUIFlags.AttentionType.pytorch;
        rbAttnSplit.Checked = Settings.Flags.Attention == ComfyUIFlags.AttentionType.split;
        rbAttnQuad.Checked = Settings.Flags.Attention == ComfyUIFlags.AttentionType.quad;
        rbAttnSage.Checked = Settings.Flags.Attention == ComfyUIFlags.AttentionType.sage;
        rbAttnFlash.Checked = Settings.Flags.Attention == ComfyUIFlags.AttentionType.flash;

        // Hardware
        rbVramNormal.Checked = Settings.Flags.VramMode == ComfyUIFlags.VramPreset.normalvram;
        rbVramHigh.Checked = Settings.Flags.VramMode == ComfyUIFlags.VramPreset.highvram;
        rbVramLow.Checked = Settings.Flags.VramMode == ComfyUIFlags.VramPreset.lowvram;
        rbVramNo.Checked = Settings.Flags.VramMode == ComfyUIFlags.VramPreset.novram;
        rbProcDefault.Checked = Settings.Flags.Processor == ComfyUIFlags.ProcessingUnit.default_gpu;
        rbProcGpuOnly.Checked = Settings.Flags.Processor == ComfyUIFlags.ProcessingUnit.gpu_only;
        rbProcCpu.Checked = Settings.Flags.Processor == ComfyUIFlags.ProcessingUnit.cpu;
        numCudaDevice.Value = Settings.Flags.CudaDevice;

        // Advanced
        chkDisableCustomNodes.Checked = Settings.Flags.DisableAllCustomNodes;
        cmbVerboseLevel.SelectedItem = Settings.Flags.VerboseLevel.ToString();
        txtOutputDir.Text = Settings.Flags.OutputDirectory;
        txtExtraModelsPath.Text = Settings.Flags.ExtraModelPathsConfig;
        txtFrontEndVersion.Text = Settings.Flags.CustomFrontEndVersion;
        chkFrontEndVersionLatest.Checked = Settings.Flags.UseLatestFrontEnd;
        txtFrontEndVersion.Enabled = !chkFrontEndVersionLatest.Checked;
    }

    private void SaveSettingsFromControls()
    {
        // Manager
        Settings.ComfyUIPath = txtComfyUIPath.Text;
        Settings.AutoStartServerOnLaunch = chkManagerAutoStart.Checked;
        Settings.AutoRestartOnCrash = chkManagerAutoRestart.Checked;
        Settings.LaunchOnWindowsStart = chkManagerLaunchOnWin.Checked;

        // General
        Settings.Flags.Port = (int)numPort.Value;
        Settings.Flags.DisableAutoLaunch = chkDisableAutoLaunch.Checked;
        Settings.Flags.DisableMetadata = chkDisableMetadata.Checked;
        Settings.Flags.MultiUser = chkMultiUser.Checked;
        Settings.Flags.DontPrintServer = chkDontPrintServer.Checked;

        // Performance
        Settings.Flags.ForceFp16 = chkForceFp16.Checked;
        Settings.Flags.ForceFp32 = chkForceFp32.Checked;
        Settings.Flags.DisableXformers = chkDisableXformers.Checked;
        if (rbAttnSplit.Checked) Settings.Flags.Attention = ComfyUIFlags.AttentionType.split;
        else if (rbAttnQuad.Checked) Settings.Flags.Attention = ComfyUIFlags.AttentionType.quad;
        else if (rbAttnSage.Checked) Settings.Flags.Attention = ComfyUIFlags.AttentionType.sage;
        else if (rbAttnFlash.Checked) Settings.Flags.Attention = ComfyUIFlags.AttentionType.flash;
        else Settings.Flags.Attention = ComfyUIFlags.AttentionType.pytorch;

        // Hardware
        if (rbVramHigh.Checked) Settings.Flags.VramMode = ComfyUIFlags.VramPreset.highvram;
        else if (rbVramLow.Checked) Settings.Flags.VramMode = ComfyUIFlags.VramPreset.lowvram;
        else if (rbVramNo.Checked) Settings.Flags.VramMode = ComfyUIFlags.VramPreset.novram;
        else Settings.Flags.VramMode = ComfyUIFlags.VramPreset.normalvram;
        if (rbProcGpuOnly.Checked) Settings.Flags.Processor = ComfyUIFlags.ProcessingUnit.gpu_only;
        else if (rbProcCpu.Checked) Settings.Flags.Processor = ComfyUIFlags.ProcessingUnit.cpu;
        else Settings.Flags.Processor = ComfyUIFlags.ProcessingUnit.default_gpu;
        Settings.Flags.CudaDevice = (int)numCudaDevice.Value;

        // Advanced
        Settings.Flags.DisableAllCustomNodes = chkDisableCustomNodes.Checked;
        Settings.Flags.VerboseLevel = cmbVerboseLevel.SelectedItem != null
            ? (ComfyUIFlags.LogLevel)Enum.Parse(typeof(ComfyUIFlags.LogLevel), (string)cmbVerboseLevel.SelectedItem)
            : ComfyUIFlags.LogLevel.INFO;
        Settings.Flags.OutputDirectory = txtOutputDir.Text;
        Settings.Flags.ExtraModelPathsConfig = txtExtraModelsPath.Text;
        Settings.Flags.CustomFrontEndVersion = txtFrontEndVersion.Text;
        Settings.Flags.UseLatestFrontEnd = chkFrontEndVersionLatest.Checked;
    }

    private void InitializeComponent()
    {
        Text = "ComfyUI Server Manager Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(520, 450);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var tabControl = new TabControl { Dock = DockStyle.Top, Height = 380 };

        // --- General Tab ---
        var generalPage = new TabPage("General");
        txtComfyUIPath = new TextBox { Location = new Point(130, 12), Size = new Size(270, 20) };
        var btnBrowsePath = new Button { Text = "Browse...", Location = new Point(410, 10), Size = new Size(80, 25) };
        var managerGroup = new GroupBox
            { Text = "Manager Settings", Location = new Point(10, 50), Size = new Size(480, 80) };
        chkManagerAutoStart = new CheckBox
            { Text = "Auto-Start Server on Launch", Location = new Point(15, 20), Size = new Size(220, 20) };
        chkManagerAutoRestart = new CheckBox
            { Text = "Auto-Restart on Crash", Location = new Point(15, 45), Size = new Size(220, 20) };
        chkManagerLaunchOnWin = new CheckBox
            { Text = "Launch Manager on Windows Start", Location = new Point(250, 20), Size = new Size(220, 20) };
        managerGroup.Controls.AddRange(new Control[]
            { chkManagerAutoStart, chkManagerAutoRestart, chkManagerLaunchOnWin });
        var serverGroup = new GroupBox
            { Text = "Server Settings", Location = new Point(10, 140), Size = new Size(480, 150) };
        numPort = new NumericUpDown
            { Location = new Point(120, 22), Size = new Size(100, 20), Minimum = 1, Maximum = 65535 };
        chkDisableAutoLaunch = new CheckBox
            { Text = "Disable Auto-Launch Browser", Location = new Point(15, 50), Size = new Size(200, 20) };
        chkDisableMetadata = new CheckBox
            { Text = "Disable Metadata in Images", Location = new Point(15, 75), Size = new Size(200, 20) };
        chkMultiUser = new CheckBox
            { Text = "Enable Multi-User Mode", Location = new Point(250, 50), Size = new Size(200, 20) };
        chkDontPrintServer = new CheckBox
            { Text = "Don't Print Server Info", Location = new Point(250, 75), Size = new Size(200, 20) };
        serverGroup.Controls.AddRange(new Control[]
        {
            new Label { Text = "Port:", Location = new Point(15, 24) }, numPort, chkDisableAutoLaunch,
            chkDisableMetadata, chkMultiUser, chkDontPrintServer
        });
        generalPage.Controls.AddRange(new Control[]
        {
            new Label { Text = "ComfyUI Path:", Location = new Point(10, 14) }, txtComfyUIPath, btnBrowsePath,
            managerGroup, serverGroup
        });
        tabControl.TabPages.Add(generalPage);

        // --- Performance Tab ---
        var perfPage = new TabPage("Performance");
        var precisionGroup = new GroupBox
            { Text = "Precision", Location = new Point(10, 10), Size = new Size(240, 80) };
        chkForceFp16 = new CheckBox { Text = "Force FP16", Location = new Point(15, 20), Size = new Size(200, 20) };
        chkForceFp32 = new CheckBox { Text = "Force FP32", Location = new Point(15, 45), Size = new Size(200, 20) };
        precisionGroup.Controls.AddRange(new Control[] { chkForceFp16, chkForceFp32 });
        var attnGroup = new GroupBox
            { Text = "Attention Type", Location = new Point(10, 100), Size = new Size(480, 80) };
        rbAttnPytorch = new RadioButton
            { Text = "Pytorch (Default)", Location = new Point(15, 20), Checked = true, AutoSize = true };
        rbAttnSplit = new RadioButton { Text = "Split", Location = new Point(180, 20) };
        rbAttnQuad = new RadioButton { Text = "Quad", Location = new Point(345, 20) };
        rbAttnSage = new RadioButton { Text = "Sage", Location = new Point(15, 45) };
        rbAttnFlash = new RadioButton { Text = "Flash", Location = new Point(180, 45) };
        attnGroup.Controls.AddRange(new Control[] { rbAttnPytorch, rbAttnSplit, rbAttnQuad, rbAttnSage, rbAttnFlash });
        chkDisableXformers = new CheckBox
            { Text = "Disable xFormers", Location = new Point(260, 30), Size = new Size(200, 20) };
        perfPage.Controls.AddRange(new Control[] { precisionGroup, attnGroup, chkDisableXformers });
        tabControl.TabPages.Add(perfPage);

        // --- Hardware Tab ---
        var hwPage = new TabPage("Hardware");
        var procGroup = new GroupBox
            { Text = "Processing Unit", Location = new Point(10, 10), Size = new Size(480, 50) };
        rbProcDefault = new RadioButton
            { Text = "Default (GPU)", Location = new Point(15, 20), Checked = true, AutoSize = true };
        rbProcGpuOnly = new RadioButton { Text = "GPU Only", Location = new Point(180, 20) };
        rbProcCpu = new RadioButton { Text = "CPU Only", Location = new Point(345, 20) };
        procGroup.Controls.AddRange(new Control[] { rbProcDefault, rbProcGpuOnly, rbProcCpu });
        var vramGroup = new GroupBox
            { Text = "VRAM Usage Preset (GPU only)", Location = new Point(10, 70), Size = new Size(480, 50) };
        rbVramNormal = new RadioButton { Text = "Normal", Location = new Point(15, 20), Checked = true };
        rbVramHigh = new RadioButton { Text = "High", Location = new Point(120, 20) };
        rbVramLow = new RadioButton { Text = "Low", Location = new Point(225, 20) };
        rbVramNo = new RadioButton { Text = "No VRAM (Slow)", Location = new Point(330, 20), AutoSize = true };
        vramGroup.Controls.AddRange(new Control[] { rbVramNormal, rbVramHigh, rbVramLow, rbVramNo });
        numCudaDevice = new NumericUpDown
            { Location = new Point(120, 142), Size = new Size(100, 20), Minimum = 0, Maximum = 16 };
        hwPage.Controls.AddRange(new Control[]
        {
            procGroup, vramGroup, new Label { Text = "CUDA Device ID:", Location = new Point(10, 144) }, numCudaDevice
        });
        tabControl.TabPages.Add(hwPage);

        // --- Advanced Tab ---
        var advancedPage = new TabPage("Advanced");
        chkDisableCustomNodes = new CheckBox
            { Text = "Disable All Custom Nodes", Location = new Point(15, 20), Size = new Size(200, 20) };
        cmbVerboseLevel = new ComboBox { Location = new Point(120, 48), DropDownStyle = ComboBoxStyle.DropDownList };
        cmbVerboseLevel.Items.AddRange(Enum.GetNames(typeof(ComfyUIFlags.LogLevel)));
        txtOutputDir = new TextBox { Location = new Point(160, 82), Size = new Size(240, 20) };
        var btnBrowseOutput = new Button { Text = "Browse...", Location = new Point(410, 80), Size = new Size(80, 25) };
        txtExtraModelsPath = new TextBox { Location = new Point(160, 112), Size = new Size(240, 20) };
        var btnBrowseModels = new Button
            { Text = "Browse...", Location = new Point(410, 110), Size = new Size(80, 25) };

        txtFrontEndVersion = new TextBox { Location = new Point(340, 142), Size = new Size(65, 20) };
        chkFrontEndVersionLatest = new CheckBox
            { Text = "latest", Location = new Point(410, 142), Size = new Size(70, 20) };
        var lblFrontEndPrefix = new Label
            { Text = "Comfy-Org/ComfyUI_frontend@", Location = new Point(160, 144), AutoSize = true };

        chkFrontEndVersionLatest.CheckedChanged += (s, e) =>
        {
            txtFrontEndVersion.Enabled = !chkFrontEndVersionLatest.Checked;
        };

        advancedPage.Controls.AddRange(new Control[]
        {
            chkDisableCustomNodes,
            new Label { Text = "Verbose Level:", Location = new Point(15, 50) }, cmbVerboseLevel,
            new Label { Text = "Output Directory:", Location = new Point(15, 84) }, txtOutputDir, btnBrowseOutput,
            new Label { Text = "Extra Models Config:", Location = new Point(15, 114) }, txtExtraModelsPath,
            btnBrowseModels,
            new Label { Text = "Front-End Version:", Location = new Point(15, 144) }, lblFrontEndPrefix,
            txtFrontEndVersion, chkFrontEndVersionLatest
        });
        tabControl.TabPages.Add(advancedPage);

        var btnOk = new Button
            { Text = "OK", Location = new Point(340, 400), Size = new Size(75, 25), DialogResult = DialogResult.OK };
        var btnCancel = new Button
        {
            Text = "Cancel", Location = new Point(425, 400), Size = new Size(75, 25), DialogResult = DialogResult.Cancel
        };

        // START: Modified Version Label Logic
        // Get AssemblyInformationalVersion, which contains the full SemVer string.
        var assembly = Assembly.GetExecutingAssembly();
        var fullVersionString = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Fallback to the standard assembly version if the informational one isn't present.
        if (string.IsNullOrEmpty(fullVersionString))
        {
            fullVersionString = assembly.GetName().Version?.ToString();
        }

        // Parse the full version string to get only the Major.Minor.Patch part (e.g., "1.0.1").
        var simpleVersion = fullVersionString?.Split('-').FirstOrDefault()?.Split('+').FirstOrDefault() ?? "N/A";

        var versionLabel = new Label
        {
            Text = $"Version: {simpleVersion}",
            // Align left (15px), and vertically center with the buttons (Y=400, H=25).
            Location = new Point(15, 406),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        
        btnBrowsePath.Click += (s, e) =>
        {
            using (var fbd = new FolderBrowserDialog
                       { Description = "Select ComfyUI portable folder", SelectedPath = txtComfyUIPath.Text })
            {
                if (fbd.ShowDialog() == DialogResult.OK) txtComfyUIPath.Text = fbd.SelectedPath;
            }
        };
        btnBrowseOutput.Click += (s, e) =>
        {
            using (var fbd = new FolderBrowserDialog
                       { Description = "Select default output directory", SelectedPath = txtOutputDir.Text })
            {
                if (fbd.ShowDialog() == DialogResult.OK) txtOutputDir.Text = fbd.SelectedPath;
            }
        };
        btnBrowseModels.Click += (s, e) =>
        {
            using (var ofd = new OpenFileDialog
                   {
                       Title = "Select extra_model_paths.yaml",
                       Filter = "YAML files (*.yaml)|*.yaml|All files (*.*)|*.*"
                   })
            {
                if (ofd.ShowDialog() == DialogResult.OK) txtExtraModelsPath.Text = ofd.FileName;
            }
        };
        btnOk.Click += (s, e) =>
        {
            SaveSettingsFromControls();
            DialogResult = DialogResult.OK;
            Close();
        };
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange(new Control[] { tabControl, btnOk, btnCancel, versionLabel });
    }
}

public class LogForm : Form
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);

    private bool _isScrollFrozen;
    private LogRichTextBox logBox = default!;

    public LogForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        logBox = new LogRichTextBox
        {
            BackColor = Color.FromArgb(12, 12, 12), BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.75F), ForeColor = Color.FromArgb(204, 204, 204), ReadOnly = true
        };
        ClientSize = new Size(784, 461);
        Controls.Add(logBox);
        Name = "LogForm";
        Text = "ComfyUI Server Logs";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ShowInTaskbar = false; // Hide from taskbar
        MinimizeBox = false; // Hide minimize button
        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        logBox.VScroll += LogBox_VScroll;
    }

    private void LogBox_VScroll(object? sender, EventArgs e)
    {
        _isScrollFrozen = !logBox.IsNearBottom();
    }

    /// <summary>
    /// Populates the log box with a large string of historical logs.
    /// </summary>
    public void SetInitialLogContent(string fullLog)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(SetInitialLogContent), fullLog);
            return;
        }

        logBox.Clear();
        // This is a simplified approach. For very large logs, a line-by-line append would be more memory efficient.
        // However, for typical use, this is fast and effective.
        ProcessAndAppend(fullLog, false);
    }

    /// <summary>
    /// Appends a single new line of text to the log box.
    /// </summary>
    public void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(AppendLog), text);
            return;
        }

        ProcessAndAppend(text, true);
    }

    /// <summary>
    /// Core logic to process a string for ANSI color codes and append it to the RichTextBox.
    /// </summary>
    private void ProcessAndAppend(string text, bool addNewLine)
    {
        if (string.IsNullOrEmpty(text)) return;

        logBox.SuspendPainting();

        var lastIndex = 0;
        var defaultColor = Color.FromArgb(204, 204, 204);
        var currentColor = defaultColor;
        foreach (Match match in AnsiRegex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                logBox.SelectionColor = currentColor;
                logBox.AppendText(text.Substring(lastIndex, match.Index - lastIndex));
            }

            currentColor = GetColorFromAnsiCode(match.Value, defaultColor);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            logBox.SelectionColor = currentColor;
            logBox.AppendText(text.Substring(lastIndex));
        }

        if (addNewLine) logBox.AppendText(Environment.NewLine);

        logBox.ResumePainting();

        // The jittery scrolling happens because ScrollToCaret() is called immediately after
        // appending text, but the RichTextBox might not have fully updated its internal
        // layout and scrollbars yet. By using BeginInvoke, we schedule the scroll operation
        // to occur after the current message processing is complete, ensuring the layout
        // is up-to-date and the scroll goes to the correct final position.
        // A check for IsHandleCreated is important as BeginInvoke requires a window handle.
        if (IsHandleCreated)
            BeginInvoke((MethodInvoker)delegate
            {
                // Check if the control is still around when the delegate runs.
                if (!logBox.IsDisposed && !_isScrollFrozen)
                {
                    logBox.SelectionStart = logBox.TextLength;
                    logBox.ScrollToCaret();
                }
            });
    }

    private Color GetColorFromAnsiCode(string ansi, Color def)
    {
        switch (ansi)
        {
            // --- Dark Colors ---
            case "\x1B[31m": return Color.DarkRed;
            case "\x1B[32m": return Color.DarkGreen;
            case "\x1B[33m": return Color.DarkGoldenrod;
            case "\x1B[34m": return Color.DarkBlue;
            case "\x1B[35m": return Color.DarkMagenta;
            case "\x1B[36m": return Color.DarkCyan;

            // --- Bright Colors ---
            case "\x1B[91m": return Color.Red;
            case "\x1B[92m": return Color.Green;
            case "\x1B[93m": return Color.Yellow;
            case "\x1B[94m": return Color.Blue;
            case "\x1B[95m": return Color.Magenta;
            case "\x1B[96m": return Color.Cyan;

            // --- Shade Colors ---
            case "\x1B[37m": return Color.Gray;
            case "\x1B[90m": return Color.DarkGray;
            case "\x1B[97m": return Color.White;

            // --- Reset Code ---
            case "\x1B[0m":
            default:
                return def;
        }
    }
}

#endregion