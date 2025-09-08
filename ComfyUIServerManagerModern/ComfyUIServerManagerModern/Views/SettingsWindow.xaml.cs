using ComfyUIServerManagerModern.ViewModels;
using Microsoft.UI.Xaml;

namespace ComfyUIServerManagerModern.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;

        // Set up the modern title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null); // Use the whole window as the title bar area
    }

    public SettingsViewModel ViewModel { get; }

    // This property will be used to signal if the user saved the settings.
    public bool WasSaved { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = false;
        Close();
    }
}