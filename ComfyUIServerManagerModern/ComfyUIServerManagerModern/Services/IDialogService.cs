using System.Threading.Tasks;

namespace ComfyUIServerManagerModern.Services;

public interface IDialogService
{
    Task ShowAlertAsync(string title, string message);
    // You could add more, like:
    // Task<bool> ShowConfirmationAsync(string title, string message);
}