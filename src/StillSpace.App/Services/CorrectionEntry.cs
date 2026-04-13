using System.Text.Json.Serialization;

namespace StillSpace.Services;

public sealed class CorrectionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("at")]
    public long At { get; set; }

    [JsonPropertyName("mistaken")]
    public string Mistaken { get; set; } = "";

    [JsonPropertyName("corrected")]
    public string Corrected { get; set; } = "";

    [JsonPropertyName("context")]
    public string? Context { get; set; }
}
