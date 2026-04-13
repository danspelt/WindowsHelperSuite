namespace StillSpace.Services;

/// <summary>Maps to Server VAD timing — shorter silence = snappier replies but more risk of cutting off mid-thought.</summary>
public enum RealtimeResponsivenessPreset
{
    Fast,
    Balanced,
    Patient
}

/// <summary>OpenAI Realtime server_vad parameters per preset.</summary>
public static class RealtimeVadParameters
{
    public static (int silenceMs, int prefixMs, double threshold) For(RealtimeResponsivenessPreset p) =>
        p switch
        {
            RealtimeResponsivenessPreset.Fast => (380, 220, 0.45),
            RealtimeResponsivenessPreset.Balanced => (480, 260, 0.48),
            RealtimeResponsivenessPreset.Patient => (920, 400, 0.42),
            _ => (480, 260, 0.48)
        };
}
