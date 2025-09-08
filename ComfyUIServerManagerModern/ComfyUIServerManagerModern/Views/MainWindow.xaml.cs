// (In the Views folder)

using Microsoft.UI.Xaml;
using WinUIEx;

namespace ComfyUIServerManagerModern.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.Hide(); // Hide the main window immediately
    }
}