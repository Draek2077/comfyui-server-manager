using ComfyUIServerManagerModern.Models;

namespace ComfyUIServerManagerModern.Services;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }

    void LoadSettings();

    void SaveSettings();

    void UpdateStartupRegistry();
}