using System.Text.Json;
using ComfyUIServerManagerModern.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ComfyUIServerManagerModern.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    // A copy of the original settings to revert to on cancel.
    private readonly AppSettings _originalSettings;

    [ObservableProperty] private AppSettings _settings;

    public SettingsViewModel(AppSettings currentSettings)
    {
        // Deep clone the settings object to prevent live updates before saving.
        // This is the simplest way to create an independent copy for editing.
        var serialized = JsonSerializer.Serialize(currentSettings);
        Settings = JsonSerializer.Deserialize<AppSettings>(serialized)!;

        // Keep a reference to the original for comparison or potential revert.
        _originalSettings = currentSettings;
    }
}