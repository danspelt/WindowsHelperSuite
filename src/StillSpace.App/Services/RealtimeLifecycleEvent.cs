namespace StillSpace.Services;

public static class RealtimeLifecyclePhase
{
    public const string Listening = "listening";
    public const string SpeechStarted = "speech_started";
    public const string SpeechStopped = "speech_stopped";
    public const string BufferCommitted = "buffer_committed";
    public const string ResponseCreated = "response_created";
    public const string FirstOutputAudio = "first_output_audio";
    public const string OutputAudioDone = "output_audio_done";
}

/// <param name="Phase">One of <see cref="RealtimeLifecyclePhase"/> constants.</param>
public readonly record struct RealtimeLifecycleEvent(string Phase, DateTimeOffset Utc);
