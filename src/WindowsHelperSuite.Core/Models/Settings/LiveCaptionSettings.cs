namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>
/// Persisted UI preferences for the Live Captions window.
/// </summary>
public class LiveCaptionSettings
{
    /// <summary>Caption text size in WPF device-independent pixels (48–200).</summary>
    public double FontSize { get; set; } = 96;

    /// <summary>Append finalized phrases to the transcript; when false, each final result replaces the previous.</summary>
    public bool AppendMode { get; set; } = true;

    /// <summary>Keep window above all others (WPF <c>Topmost</c>).</summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>Remember last window width/height.</summary>
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 540;

    /// <summary>IETF BCP-47 language tag for recognition (e.g. en-US).</summary>
    public string RecognitionLanguage { get; set; } = "en-US";

    // ── OpenAI Whisper Speech Recognition ──────────────────────────────────────

    /// <summary>OpenAI API key for Whisper speech-to-text; if empty, OPENAI_API_KEY env var is used.</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>Whisper model to use: gpt-4o-transcribe (default, best for atypical speech), whisper-1, or gpt-4o-mini-transcribe.</summary>
    public string OpenAiWhisperModel { get; set; } = "gpt-4o-transcribe";

    /// <summary>Transcription prompt to guide Whisper recognition (e.g. "User has cerebral palsy, speech may be slurred").</summary>
    public string OpenAiWhisperPrompt { get; set; } =
        "The speaker has cerebral palsy (dysarthric/slurred speech). Transcribe faithfully. Prefer likely intended words. Keep meaning; do not paraphrase.";

    /// <summary>
    /// Extra phrase hints, one per line (names, places, apps, medical terms, frequent phrases).
    /// These are appended to <see cref="OpenAiWhisperPrompt"/> when calling the API.
    /// </summary>
    public string OpenAiWhisperPhraseHints { get; set; } = string.Empty;

    /// <summary>
    /// Optional example sentences in the speaker's style (one per line). Helps bias transcription toward your phrasing.
    /// </summary>
    public string OpenAiWhisperExampleSentences { get; set; } = string.Empty;
}
