// (In the Services folder)

using System;
using System.IO;
using System.Text.Json;
using ComfyUIServerManagerModern.Models;
using ComfyUIServerManagerModern.Helpers;

namespace ComfyUIServerManagerModern.Services;

public class SettingsService : ISettingsService
{
    private static readonly string AppName = "ComfyUI Server Manager";
    private readonly string _settingsPath;

    public AppSettings CurrentSettings { get; private set; }

    public SettingsService()
    {
        _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "settings.json");
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                CurrentSettings = new AppSettings();
            }
        }
        catch
        {
            CurrentSettings = new AppSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(CurrentSettings, options);
                File.WriteAllText(_settingsPath, json);
            }
        }
        catch (Exception ex)
        {
            // In a real app, you'd want to log this error.
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    public void UpdateStartupRegistry()
    {
        WindowsStartupHelper.SetStartup(CurrentSettings.LaunchOnWindowsStart, AppName);
    }
}