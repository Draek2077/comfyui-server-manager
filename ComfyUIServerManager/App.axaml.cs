using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ComfyUIServerManager.Models;
using ComfyUIServerManager.Platform;
using ComfyUIServerManager.Services;
using ComfyUIServerManager.Views;

namespace ComfyUIServerManager;

public partial class App : Application
{
    private TrayIcon _trayIcon = null!;
    private NativeMenuItem _miStart = null!;
    private NativeMenuItem _miRestart = null!;
    private NativeMenuItem _miStop = null!;
    private NativeMenuItem _miLogs = null!;
    private NativeMenuItem _miAutoStart = null!;
    private NativeMenuItem _miAutoRestart = null!;
    private NativeMenuItem _miLaunchAtLogin = null!;

    private WindowIcon? _iconOn;
    private WindowIcon? _iconOff;
    private WindowIcon? _iconDefault;

    private IPlatformServices _platform = null!;
    private ServerManager _server = null!;
    private AppSettings _settings = null!;
    private LogWindow? _logWindow;
    private bool _isShuttingDown;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray-only: never auto-pick a MainWindow. The lifetime stays alive as long
            // as the TrayIcon (and any non-tray-only windows we open transiently) hold
            // it open.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => OnExit();

            // During shutdown, Avalonia's StatusNotifierItem/DBus menu exporter can race
            // and throw NREs from queued DoLayoutReset ops after we've torn the menu down.
            // The process is exiting anyway; swallow them so the user doesn't see a stack
            // trace in their terminal. Don't swallow during normal operation.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (_isShuttingDown) return;
                Console.Error.WriteLine(e.ExceptionObject);
            };

            _platform = PlatformServicesFactory.Create();
            LoadIcons();
            _settings = SettingsManager.Load();
            _server = new ServerManager(_platform) { Settings = _settings };
            _server.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateMenuState);
            _server.LogLineReceived += (_, e) =>
                Dispatcher.UIThread.Post(() => _logWindow?.AppendLog(e.Line));

            BuildTrayMenu();

            var firstRun = !SettingsManager.SettingsFileExists() || string.IsNullOrWhiteSpace(_settings.ComfyUIPath);
            if (firstRun)
            {
                // First run: open settings before doing anything else.
                Dispatcher.UIThread.Post(OpenSettings);
            }
            else
            {
                var attached = _server.TryAttachToExistingProcess();
                if (!attached && _settings.AutoStartServerOnLaunch) _server.Start();
            }

            UpdateMenuState();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // --- Icons -----------------------------------------------------------------

    private void LoadIcons()
    {
        _iconOn = TryLoadIcon("Comfy_On.ico");
        _iconOff = TryLoadIcon("Comfy_Off.ico");
        _iconDefault = TryLoadIcon("Comfy_Logo.ico");
    }

    private static WindowIcon? TryLoadIcon(string assetName)
    {
        try
        {
            var uri = new Uri($"avares://ComfyUIServerManager/{assetName}");
            using var stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    // --- Tray menu -------------------------------------------------------------

    private void BuildTrayMenu()
    {
        _miStart = new NativeMenuItem("Start ComfyUI Server");
        _miStart.Click += (_, _) => _server.Start();

        _miRestart = new NativeMenuItem("Restart ComfyUI Server");
        _miRestart.Click += (_, _) => _server.Restart();

        _miStop = new NativeMenuItem("Stop ComfyUI Server");
        _miStop.Click += (_, _) => _server.Stop();

        _miLogs = new NativeMenuItem("View Server Logs") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _miLogs.Click += (_, _) => ToggleLogs();

        _miAutoStart = new NativeMenuItem("Auto-Start Server on Launch")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _settings.AutoStartServerOnLaunch
        };
        _miAutoStart.Click += (_, _) =>
        {
            _settings.AutoStartServerOnLaunch = !_settings.AutoStartServerOnLaunch;
            SettingsManager.Save(_settings);
            UpdateMenuState();
        };

        _miAutoRestart = new NativeMenuItem("Auto-Restart Server on Crash")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _settings.AutoRestartOnCrash
        };
        _miAutoRestart.Click += (_, _) =>
        {
            _settings.AutoRestartOnCrash = !_settings.AutoRestartOnCrash;
            SettingsManager.Save(_settings);
            UpdateMenuState();
        };

        _miLaunchAtLogin = new NativeMenuItem(_platform.LaunchAtLoginLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _platform.IsLaunchAtLoginEnabled()
        };
        _miLaunchAtLogin.Click += (_, _) =>
        {
            var next = !_platform.IsLaunchAtLoginEnabled();
            try { _platform.SetLaunchAtLogin(next, Environment.ProcessPath ?? "comfyui-server-manager"); }
            catch (Exception ex) { /* swallow — non-fatal */ Console.Error.WriteLine(ex); }
            _settings.LaunchOnSystemStart = _platform.IsLaunchAtLoginEnabled();
            SettingsManager.Save(_settings);
            UpdateMenuState();
        };

        var miSettings = new NativeMenuItem("Settings...");
        miSettings.Click += (_, _) => OpenSettings();

        var miExit = new NativeMenuItem("Exit");
        miExit.Click += (_, _) => ExitApp();

        var menu = new NativeMenu();
        menu.Items.Add(_miStart);
        menu.Items.Add(_miRestart);
        menu.Items.Add(_miStop);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_miLogs);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_miAutoStart);
        menu.Items.Add(_miAutoRestart);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_miLaunchAtLogin);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(miSettings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(miExit);

        _trayIcon = new TrayIcon
        {
            Icon = _iconOff ?? _iconDefault,
            ToolTipText = "ComfyUI Server Manager (Stopped)",
            Menu = menu,
            IsVisible = true
        };

        // Left-click brings the log window forward (or shows it if hidden), to match
        // the WinForms behaviour. Avalonia's TrayIcon.Clicked is left-click only on
        // both platforms.
        _trayIcon.Clicked += (_, _) => ToggleLogs();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void UpdateMenuState()
    {
        var s = _server.State;
        _miStart.IsEnabled = s == ServerState.Stopped;
        _miRestart.IsEnabled = s == ServerState.Running;
        _miStop.IsEnabled = s is ServerState.Running or ServerState.Starting;
        _miLogs.IsChecked = _logWindow?.IsVisible == true;
        _miAutoStart.IsChecked = _settings.AutoStartServerOnLaunch;
        _miAutoRestart.IsChecked = _settings.AutoRestartOnCrash;
        _miLaunchAtLogin.IsChecked = _platform.IsLaunchAtLoginEnabled();

        switch (s)
        {
            case ServerState.Running:
                _trayIcon.Icon = _iconOn ?? _iconDefault;
                _trayIcon.ToolTipText = $"ComfyUI Server Manager (Running — PID: {_server.CurrentPid})";
                break;
            case ServerState.Starting:
                _trayIcon.Icon = _iconDefault ?? _iconOff;
                _trayIcon.ToolTipText = "ComfyUI Server Manager (Starting...)";
                break;
            default:
                _trayIcon.Icon = _iconOff ?? _iconDefault;
                _trayIcon.ToolTipText = "ComfyUI Server Manager (Stopped)";
                break;
        }
    }

    // --- Windows ---------------------------------------------------------------

    private void ToggleLogs()
    {
        if (_logWindow == null)
        {
            _logWindow = new LogWindow();
            _logWindow.SetInitialLogContent(_server.GetLogSnapshot());
            _logWindow.Closed += (_, _) => UpdateMenuState();
        }

        if (_logWindow.IsVisible)
        {
            _logWindow.Hide();
        }
        else
        {
            _logWindow.Show();
            _logWindow.Activate();
        }
        UpdateMenuState();
    }

    private async void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(_settings, _platform);
        var result = await settingsWindow.ShowDialogAsync();
        if (result is AppSettings updated)
        {
            _settings = updated;
            _server.Settings = _settings;
            SettingsManager.Save(_settings);
            UpdateMenuState();
        }
    }

    private void ExitApp()
    {
        _isShuttingDown = true;
        _server.Stop();

        // Tear the tray icon down explicitly BEFORE Shutdown(). On Linux, Avalonia's
        // StatusNotifierItem DBus menu exporter queues async layout-reset operations
        // every time the menu changes — including when the icon is being disposed.
        // If Shutdown() is called while one of those ops is still in flight, the
        // exporter's connection is already null when the op finally runs, and Tmds.DBus
        // throws NullReferenceException from EmitLayoutUpdated, crashing the process.
        // Order matters: detach the menu first (no more layout ops will be queued),
        // then drop the tray-icons attached property entirely.
        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.Menu = null;
                _trayIcon.IsVisible = false;
            }
            TrayIcon.SetIcons(this, new TrayIcons());
        }
        catch { /* shutting down; ignore */ }

        try { _logWindow?.Close(); } catch { /* ignore */ }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Give any already-queued dbusmenu jobs a single dispatcher tick to drain
            // against the still-valid connection before we tear the dispatcher down.
            Dispatcher.UIThread.Post(() => desktop.Shutdown(), DispatcherPriority.Background);
        }
    }

    private void OnExit()
    {
        // Lifetime Exit handler — by the time this fires the dispatcher is already
        // winding down, so anything we do here has to be fire-and-forget.
        _isShuttingDown = true;
        try { _logWindow?.Close(); } catch { /* ignore */ }
    }
}
