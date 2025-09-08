// (In the ViewModels folder)
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComfyUIServerManagerModern.Services;
using ComfyUIServerManagerModern.Views;

namespace ComfyUIServerManagerModern.ViewModels;

public partial class MainViewModel(LogViewModel logViewModel) : ObservableObject
{
    [RelayCommand]
    private void OpenLogWindow()
    {
        // Create and activate the LogWindow, passing the logs to it
        var logWindow = new LogWindow(logViewModel);
        logWindow.Activate();
    }
}