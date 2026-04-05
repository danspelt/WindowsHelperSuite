using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;
using static WindowsHelperSuite.Infrastructure.Services.QuickTextSettingsService;

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
        if (!File.Exists(SettingsFilePath))
        {
            Settings = new AppSettings();
            Settings.QuickText ??= new QuickTextSettings();
            NormalizeAndSeedIfEmpty(Settings.QuickText);
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            Settings = loaded ?? new AppSettings();
            Settings.QuickText ??= new QuickTextSettings();
            NormalizeAndSeedIfEmpty(Settings.QuickText);
        }
        catch (JsonException)
        {
            TryBackupCorruptFile(SettingsFilePath);
            Settings = new AppSettings();
            Settings.QuickText ??= new QuickTextSettings();
            NormalizeAndSeedIfEmpty(Settings.QuickText);
            Save();
        }
        catch (IOException)
        {
            Settings = new AppSettings();
            Settings.QuickText ??= new QuickTextSettings();
            NormalizeAndSeedIfEmpty(Settings.QuickText);
        }
    }

    private static void TryBackupCorruptFile(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var dest = Path.Combine(dir, $"{name}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Copy(path, dest, overwrite: false);
        }
        catch
        {
            // best-effort backup only
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
        Settings.QuickText ??= new QuickTextSettings();
        NormalizeAndSeedIfEmpty(Settings.QuickText);
        Save();
    }
}
