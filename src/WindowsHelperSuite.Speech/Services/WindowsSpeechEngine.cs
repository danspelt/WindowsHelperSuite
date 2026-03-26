using System.Speech.Synthesis;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Offline TTS via installed Windows voices (System.Speech).
/// </summary>
internal sealed class WindowsSpeechEngine : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly string? _preferredVoice;
    private bool _disposed;

    public WindowsSpeechEngine(ILoggingService? log)
    {
        _preferredVoice = PickBestInstalledVoice();
        if (!string.IsNullOrWhiteSpace(_preferredVoice))
        {
            try
            {
                _synth.SelectVoice(_preferredVoice);
            }
            catch (Exception ex)
            {
                log?.Debug($"Offline speech: could not select preferred voice '{_preferredVoice}': {ex.Message}");
            }
        }
    }

    public string? PreferredVoiceName => _preferredVoice;

    public void Speak(string text, int rate, int volume, string? voiceName, ILoggingService? log)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _synth.SpeakAsyncCancelAll();
            _synth.Rate = Math.Clamp(rate, -10, 10);
            _synth.Volume = Math.Clamp(volume, 0, 100);

            var v = !string.IsNullOrWhiteSpace(voiceName) ? voiceName : _preferredVoice;
            if (!string.IsNullOrWhiteSpace(v))
            {
                try
                {
                    _synth.SelectVoice(v);
                }
                catch (Exception ex)
                {
                    log?.Debug($"Offline speech: SelectVoice failed for '{v}': {ex.Message}");
                }
            }

            _synth.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            log?.Warning($"Offline speech failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch
        {
            // ignore
        }
    }

    private static string? PickBestInstalledVoice()
    {
        try
        {
            using var probe = new SpeechSynthesizer();
            var voices = probe.GetInstalledVoices();
            var best = voices
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .OrderByDescending(v => v.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(v => v.Culture.Name.Equals("en-CA", StringComparison.OrdinalIgnoreCase))
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return best?.Name;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _synth.SpeakAsyncCancelAll();
            _synth.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
