namespace WindowsHelperSuite.Core.Interfaces;

public interface ISpeechService
{
    bool IsPreferredDeviceConnected { get; }
    void Speak(string text);
    void Stop();
}
