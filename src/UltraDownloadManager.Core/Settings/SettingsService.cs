using System.Text.Json;

namespace UltraDownloadManager.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraDownloadManager");

    private string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public string SessionFilePath => Path.Combine(AppDataDirectory, "aria2.session");

    public string LogFilePath => Path.Combine(AppDataDirectory, "logs", "aria2.log");

    public string HistoryDbPath => Path.Combine(AppDataDirectory, "history.db");

    public AppSettings Load()
    {
        Directory.CreateDirectory(AppDataDirectory);

        if (!File.Exists(SettingsFilePath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
