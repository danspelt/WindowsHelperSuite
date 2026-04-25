using System.Text.Json.Serialization;

namespace VoiceBridge.Contracts;

/// <summary>
/// Single JSON object shape for phone ↔ PC messages. Unknown fields should be ignored by each side.
/// Uses camelCase on the wire to match <see cref="System.Text.Json"/> defaults in the Windows app.
/// </summary>
public sealed class VoiceBridgeEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    /// <summary>Optional body token for <see cref="VoiceBridgeMessageTypes.PairRequest"/> when not using query-string pairing.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("options")]
    public string[]? Options { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("entities")]
    public Dictionary<string, string>? Entities { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("executed")]
    public bool? Executed { get; set; }
}
