using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ComfyUIServerManagerModern.Services;

public class DialogService : IDialogService
{
    private readonly XamlRoot _xamlRoot;

    // We'll give it the XamlRoot when we create it.
    public DialogService(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
    }

    public async Task ShowAlertAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _xamlRoot // Use the root we were given!
        };

        await dialog.ShowAsync();
    }
}