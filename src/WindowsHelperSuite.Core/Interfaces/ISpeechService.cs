namespace WindowsHelperSuite.Core.Interfaces;

public interface ISpeechService
{
    bool IsPreferredDeviceConnected { get; }

    /// <summary>Primary voice label for logging/UI (Azure voice or offline voice name).</summary>
    string VoiceName { get; }

    /// <summary>Last route used: Online, Offline fallback, Offline only, Unavailable, or Idle.</summary>
    string VoiceRouteStatus { get; }

    bool IsMutedByTypingSpeed { get; }
    void Speak(string text);
    void SpeakQueued(string text, bool ignoreTypingCooldown = false);
    void Stop();
    void ClearQueue();
    void SetRate(double rate);
    void SetVolume(float volume);
    void NotifyKeystroke();
}
