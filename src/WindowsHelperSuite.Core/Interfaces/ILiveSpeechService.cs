namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>
/// Live speech-to-text for the Captions window. Engines raise:
///  - <see cref="PartialTextReceived"/> while the user is still speaking a phrase
///  - <see cref="FinalTextReceived"/> once the recognizer settles on a sentence/segment
///  - <see cref="ErrorOccurred"/> with a short user-readable message
///  - <see cref="ListeningStateChanged"/> when capture starts/stops
/// </summary>
public interface ILiveSpeechService
{
    event EventHandler<string>? PartialTextReceived;
    event EventHandler<string>? FinalTextReceived;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<bool>? ListeningStateChanged;

    /// <summary>Human-readable label for the currently selected engine (e.g. "Azure", "Windows WinRT").</summary>
    string ActiveEngineName { get; }

    bool IsListening { get; }

    Task StartAsync(string languageTag = "en-US", CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
