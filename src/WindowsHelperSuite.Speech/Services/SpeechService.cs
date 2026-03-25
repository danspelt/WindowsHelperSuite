using System.Speech.Synthesis;
using NAudio.CoreAudioApi;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Speech.Services;

public class SpeechService : ISpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private readonly System.Timers.Timer _deviceCheckTimer;
    private readonly object _lock = new();
    private bool _headsetConnected;

    private static readonly string[] HeadsetKeywords =
    [
        "headset", "headphone", "earphone", "earbuds",
        "shokz", "bluetooth", "airpods", "jabra",
        "bose", "sony wh", "sony wf", "galaxy buds",
        "bt audio", "hands-free"
    ];

    public SpeechService()
    {
        _synth = new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = 1; // Normal speed

        // Check for headset immediately, then every 5 seconds
        _headsetConnected = DetectHeadset();

        _deviceCheckTimer = new System.Timers.Timer(5000) { AutoReset = true };
        _deviceCheckTimer.Elapsed += (_, _) => _headsetConnected = DetectHeadset();
        _deviceCheckTimer.Start();
    }

    public bool IsPreferredDeviceConnected => _headsetConnected;

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !_headsetConnected)
            return;

        lock (_lock)
        {
            try
            {
                // Cancel any in-progress speech
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TTS error: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _synth.SpeakAsyncCancelAll(); }
            catch { }
        }
    }

    private static bool DetectHeadset()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(
                DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                var name = device.FriendlyName?.ToLowerInvariant() ?? "";
                foreach (var keyword in HeadsetKeywords)
                {
                    if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Headset detection error: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        _deviceCheckTimer.Stop();
        _deviceCheckTimer.Dispose();
        Stop();
        _synth.Dispose();
    }
}
