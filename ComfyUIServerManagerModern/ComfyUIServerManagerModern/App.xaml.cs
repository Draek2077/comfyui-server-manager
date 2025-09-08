// (In the root of the project)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using ComfyUIServerManagerModern.Services;
using ComfyUIServerManagerModern.ViewModels;
using ComfyUIServerManagerModern.Views;
using H.NotifyIcon;

namespace ComfyUIServerManagerModern;

public partial class App : Application
{
    private const string AppMutexName = "ComfyUIServerManager-7E2B4E9A-3C1D-4B5F-8D9A-2C1B4E9A3C1D";
    private static Mutex? _mutex;
    public IServiceProvider Services { get; }
    public static Window? MainWindow { get; private set; }
    private TaskbarIcon? _taskbarIcon;

    public App()
    {
        // Ensure only one instance of the application is running
        _mutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running. Exit.
            Environment.Exit(0);
            return;
        }

        Services = ConfigureServices();
        this.InitializeComponent();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IProcessService, ComfyUIProcessService>();
        services.AddSingleton<ILoggingService, InMemoryLoggingService>();

        // ViewModels
        services.AddSingleton<TrayIconViewModel>();
        services.AddSingleton<LogViewModel>();
    
        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        // Initialize the Tray Icon
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "ComfyUI Server Manager",
            Icon = new System.Drawing.Icon("Assets/Comfy_Off.ico"), // Default icon
            ContextFlyout = (Microsoft.UI.Xaml.Controls.MenuFlyout)Resources["TrayMenuFlyout"]
        };

        // Set the DataContext for data binding to work
        var trayViewModel = Services.GetRequiredService<TrayIconViewModel>();
        _taskbarIcon.DataContext = trayViewModel;
        
        // This ensures the application doesn't shut down when the invisible main window is closed.
        _taskbarIcon.ForceCreate();

        MainWindow.Hide();
        MainWindow.HideInTaskbar();
        
        //MainWindow.Activate();
    }
    
}