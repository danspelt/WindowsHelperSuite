using System.Text.Json.Serialization;
using StillSpace.Writer;

namespace StillSpace.Services;

public sealed class StillSpaceSettings : IWriterOpenAiSettings
{
    [JsonPropertyName("preferredName")]
    public string PreferredName { get; set; } = "";

    [JsonPropertyName("sttLang")]
    public string SttLang { get; set; } = "en-US";

    [JsonPropertyName("autoReadAloud")]
    public bool AutoReadAloud { get; set; } = true;

    [JsonPropertyName("preferOpenAiTts")]
    public bool PreferOpenAiTts { get; set; } = true;

    [JsonPropertyName("pauseBeforeReplyMs")]
    public int PauseBeforeReplyMs { get; set; } = 400;

    [JsonPropertyName("headsetOnlyMode")]
    public bool HeadsetOnlyMode { get; set; }

    /// <summary>Substring match against render device friendly name (e.g. OpenRun, Shokz).</summary>
    [JsonPropertyName("headsetNameMatch")]
    public string HeadsetNameMatch { get; set; } = "OpenRun";

    [JsonPropertyName("preferredOutputDeviceId")]
    public string PreferredOutputDeviceId { get; set; } = "";

    [JsonPropertyName("preferredInputDeviceId")]
    public string PreferredInputDeviceId { get; set; } = "";

    /// <summary>Optional override; otherwise OPENAI_API_KEY from environment / .env.</summary>
    [JsonPropertyName("openAiApiKey")]
    public string OpenAiApiKey { get; set; } = "";

    [JsonPropertyName("openAiChatModel")]
    public string OpenAiChatModel { get; set; } = "";

    /// <summary>Model for writing-assistant “next words” hints, not the chat counselor (empty = gpt-4o-mini).</summary>
    [JsonPropertyName("openAiHintModel")]
    public string OpenAiHintModel { get; set; } = "";

    /// <summary>Show writing-assistant continuation under Heard while dictating (not the counselor; not during live voice).</summary>
    [JsonPropertyName("aiDictationNextWordHints")]
    public bool AiDictationNextWordHints { get; set; } = true;

    [JsonPropertyName("openAiTtsModel")]
    public string OpenAiTtsModel { get; set; } = "";

    [JsonPropertyName("openAiTtsVoice")]
    public string OpenAiTtsVoice { get; set; } = "alloy";

    /// <summary>Realtime voice session model (e.g. gpt-realtime). Empty = default.</summary>
    [JsonPropertyName("openAiRealtimeModel")]
    public string OpenAiRealtimeModel { get; set; } = "";

    /// <summary>Realtime output voice id (e.g. marin, alloy).</summary>
    [JsonPropertyName("openAiRealtimeVoice")]
    public string OpenAiRealtimeVoice { get; set; } = "marin";

    /// <summary>How long the server waits for silence before treating your turn as finished.</summary>
    [JsonPropertyName("realtimeResponsiveness")]
    public RealtimeResponsivenessPreset RealtimeResponsiveness { get; set; } = RealtimeResponsivenessPreset.Balanced;

    /// <summary>Show live voice turn-state strip during Realtime sessions.</summary>
    [JsonPropertyName("showRealtimeVoiceDiagnostics")]
    public bool ShowRealtimeVoiceDiagnostics { get; set; }

    /// <summary>Write per-phase timing lines to System.Diagnostics.Trace (Debug / VS Output).</summary>
    [JsonPropertyName("logRealtimeTurnTimings")]
    public bool LogRealtimeTurnTimings { get; set; }
}
