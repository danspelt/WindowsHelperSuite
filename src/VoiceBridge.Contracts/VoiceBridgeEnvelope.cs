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

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    // hello / auth
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    [JsonPropertyName("hmac")]
    public string? Hmac { get; set; }

    /// <summary>Optional body token for <see cref="VoiceBridgeMessageTypes.PairRequest"/> when not using query-string pairing.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    // commands
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public Dictionary<string, string>? Args { get; set; }

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

    // audio streaming (optional)
    [JsonPropertyName("seq")]
    public int? Seq { get; set; }

    [JsonPropertyName("audioFormat")]
    public string? AudioFormat { get; set; }

    [JsonPropertyName("sampleRate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    /// <summary>Base64 payload for <see cref="VoiceBridgeMessageTypes.AudioChunk"/>.</summary>
    [JsonPropertyName("audioBase64")]
    public string? AudioBase64 { get; set; }
}
