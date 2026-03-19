using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Settings { get; }
    string SettingsFilePath { get; }

    void Load();
    void Save();
    void ResetToDefaults();
}
