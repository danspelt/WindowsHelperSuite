using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly string _appDataPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public AppSettings Settings { get; private set; } = new();

    public string SettingsFilePath => Path.Combine(_appDataPath, "settings.json");

    public SettingsService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        Directory.CreateDirectory(_appDataPath);
    }

    public void Load()
    {
        if (File.Exists(SettingsFilePath))
        {
            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            if (loaded != null)
            {
                Settings = loaded;
            }
        }
        else
        {
            Settings = new AppSettings();
            Save();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Settings, _jsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    public void ResetToDefaults()
    {
        Settings = new AppSettings();
        Save();
    }
}
