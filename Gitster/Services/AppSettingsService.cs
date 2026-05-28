using System.IO;
using System.Text.Json;
using System.Windows;

namespace Gitster.Services;

/// <summary>
/// Persists app-wide (non-repo) settings in %AppData%/Gitster/window-settings.json.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gitster");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "window-settings.json");

    public sealed record WindowSettings(
        double Left,
        double Top,
        double Width,
        double Height,
        WindowState State);

    public WindowSettings? LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<WindowSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWindowSettings(WindowSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettingsService.Save: {ex.Message}");
        }
    }
}
