using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Speech.Services;

public class SpeechService : ISpeechService
{
    public bool IsPreferredDeviceConnected => false;

    public void Speak(string text) { }
    public void Stop() { }
}
