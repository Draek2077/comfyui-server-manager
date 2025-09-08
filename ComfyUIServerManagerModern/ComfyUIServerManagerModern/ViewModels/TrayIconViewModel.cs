// (In the ViewModels folder)
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComfyUIServerManagerModern.Services;
using ComfyUIServerManagerModern.Models;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using H.NotifyIcon;
using System.Linq;
using ComfyUIServerManagerModern.Views;

namespace ComfyUIServerManagerModern.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    // We will create this service next.
    private readonly IProcessService _processService;

    [ObservableProperty]
    private string _tooltipText = "ComfyUI Server Manager (Stopped)";

    [ObservableProperty]
    private string _iconSource = "Assets/Comfy_Off.ico";

    private readonly LogViewModel _logViewModel;

    public TrayIconViewModel(LogViewModel logViewModel, ISettingsService settingsService, IProcessService processService)
    {
        _logViewModel = logViewModel;
        _settingsService = settingsService;
        _processService = processService;

        _processService.StateChanged += OnProcessStateChanged;
        
        _processService.TryAttachToExistingProcess();
        UpdateStateProperties();

        if (_settingsService.CurrentSettings.AutoStartServerOnLaunch && _processService.CurrentState == ServerState.Stopped)
        {
            StartServer();
        }
    }

    private void OnProcessStateChanged(ServerState newState)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(UpdateStateProperties);
    }

    private void UpdateStateProperties()
    {
        switch (_processService.CurrentState)
        {
            case ServerState.Running:
                TooltipText = $"ComfyUI Server Manager (Running - PID: {_processService.ProcessId})";
                IconSource = "Assets/Comfy_On.ico";
                break;
            case ServerState.Starting:
                TooltipText = "ComfyUI Server Manager (Starting...)";
                IconSource = "Assets/Comfy_On.ico"; // Or a dedicated "pending" icon
                break;
            case ServerState.Stopped:
            default:
                TooltipText = "ComfyUI Server Manager (Stopped)";
                IconSource = "Assets/Comfy_Off.ico";
                break;
        }
        // Notify command states might change
        StartServerCommand.NotifyCanExecuteChanged();
        StopServerCommand.NotifyCanExecuteChanged();
        RestartServerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartServer))]
    private void StartServer() => _processService.Start();

    private bool CanStartServer() => _processService.CurrentState == ServerState.Stopped;

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private void StopServer() => _processService.Stop();

    private bool CanStopServer() => _processService.CurrentState != ServerState.Stopped;

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private void RestartServer() => _processService.Restart();

    [RelayCommand]
    private void ShowLogs()
    {
        var logWindow = new LogWindow(_logViewModel);
        logWindow.Activate();
    }
    
    [RelayCommand]
    private void ShowSettings()
    {
        // This is where you would open your SettingsWindow.
        // We will add this functionality later.
    }

    [RelayCommand]
    private void ExitApplication()
    {
        _processService.Stop();
        Application.Current.Exit();
    }
    
    // Properties for ToggleMenuFlyoutItem binding
    public bool AutoStartServer
    {
        get => _settingsService.CurrentSettings.AutoStartServerOnLaunch;
        set
        {
            if (_settingsService.CurrentSettings.AutoStartServerOnLaunch != value)
            {
                _settingsService.CurrentSettings.AutoStartServerOnLaunch = value;
                OnPropertyChanged();
                _settingsService.SaveSettings();
            }
        }
    }

    public bool AutoRestartOnCrash
    {
        get => _settingsService.CurrentSettings.AutoRestartOnCrash;
        set
        {
            if (_settingsService.CurrentSettings.AutoRestartOnCrash != value)
            {
                _settingsService.CurrentSettings.AutoRestartOnCrash = value;
                OnPropertyChanged();
                _settingsService.SaveSettings();
            }
        }
    }

    public bool LaunchOnWindowsStart
    {
        get => _settingsService.CurrentSettings.LaunchOnWindowsStart;
        set
        {
            if (_settingsService.CurrentSettings.LaunchOnWindowsStart != value)
            {
                _settingsService.CurrentSettings.LaunchOnWindowsStart = value;
                OnPropertyChanged();
                _settingsService.SaveSettings();
                _settingsService.UpdateStartupRegistry();
            }
        }
    }
}