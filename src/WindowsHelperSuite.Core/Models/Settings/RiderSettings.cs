namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>
/// Configuration for the Voice Rider - a voice-activated assistant that "rides along"
/// with the user and only responds to specific wake triggers.
/// </summary>
public sealed class RiderSettings
{
    /// <summary>
    /// Enable or disable the voice rider system.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The wake phrase that activates the rider when spoken.
    /// Default: "I is king"
    /// </summary>
    public string WakePhrase { get; set; } = "I is king";

    /// <summary>
    /// The hotkey gesture that activates the rider.
    /// Only this key can wake the rider once it's active.
    /// Default: "`" (backtick)
    /// </summary>
    public string WakeHotkeyGesture { get; set; } = "`";

    /// <summary>
    /// When true, only the wake-up key can activate the rider.
    /// No other keys or triggers will activate it.
    /// </summary>
    public bool WakeKeyOnly { get; set; } = true;

    /// <summary>
    /// Once the rider is activated by the wake phrase, 
    /// it remains active and responds only to the wake-up key.
    /// </summary>
    public bool StayActiveAfterWakePhrase { get; set; } = true;

    /// <summary>
    /// Timeout in seconds after which the rider goes back to sleep
    /// if no wake key is pressed. 0 = no timeout.
    /// </summary>
    public int SleepTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// Require exact match for wake phrase (case insensitive).
    /// When false, partial matches are accepted.
    /// </summary>
    public bool ExactWakePhraseMatch { get; set; } = true;

    /// <summary>
    /// Minimum confidence level (0.0-1.0) for voice recognition
    /// to accept the wake phrase.
    /// </summary>
    public double WakePhraseConfidenceThreshold { get; set; } = 0.85;

    /// <summary>
    /// When true, the rider consumes the wake key press so it doesn't
    /// pass through to other applications.
    /// </summary>
    public bool ConsumeWakeKey { get; set; } = true;
}
