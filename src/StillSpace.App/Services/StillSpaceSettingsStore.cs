using System.Text.Json;
using System.Text.Json.Serialization;

namespace StillSpace.Services;

public sealed class StillSpaceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public StillSpaceSettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillSpace");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public StillSpaceSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new StillSpaceSettings();
            var json = File.ReadAllText(_path);
            var s = JsonSerializer.Deserialize<StillSpaceSettings>(json, JsonOpts) ?? new StillSpaceSettings();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("realtimeResponsiveness", out _)
                    && doc.RootElement.TryGetProperty("realtimeSlowSpeakerVad", out var leg)
                    && leg.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    s.RealtimeResponsiveness = leg.GetBoolean()
                        ? RealtimeResponsivenessPreset.Patient
                        : RealtimeResponsivenessPreset.Fast;
                }
            }
            catch
            {
                /* ignore migration */
            }

            return s;
        }
        catch
        {
            return new StillSpaceSettings();
        }
    }

    public void Save(StillSpaceSettings s) => File.WriteAllText(_path, JsonSerializer.Serialize(s, JsonOpts));
}
