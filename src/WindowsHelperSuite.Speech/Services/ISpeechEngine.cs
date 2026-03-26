namespace WindowsHelperSuite.Speech.Services;

internal interface ISpeechEngine
{
    string EngineName { get; }
    bool IsAvailable();
    Task<byte[]?> SynthesizeAsync(string text, string voiceName, double rate);
    void Stop();
}
