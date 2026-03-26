using System.Net.NetworkInformation;
using Microsoft.CognitiveServices.Speech;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.Services;

internal sealed class AzureSpeechEngine
{
    private readonly object _gate = new();
    private SpeechSynthesizer? _active;

    public static bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

    public static (string? Key, string? Region) ResolveCredentials(SpeechSettings settings)
    {
        var key = !string.IsNullOrWhiteSpace(settings.AzureSpeechKey)
            ? settings.AzureSpeechKey.Trim()
            : Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")
              ?? Environment.GetEnvironmentVariable("SPEECH_KEY");

        var region = !string.IsNullOrWhiteSpace(settings.AzureSpeechRegion)
            ? settings.AzureSpeechRegion.Trim()
            : Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
              ?? Environment.GetEnvironmentVariable("SPEECH_REGION");

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            return (null, null);
        }

        return (key, region);
    }

    public bool IsConfigured(SpeechSettings settings)
    {
        var (key, region) = ResolveCredentials(settings);
        return key != null && region != null;
    }

    public void StopSpeaking()
    {
        SpeechSynthesizer? synth;
        lock (_gate)
        {
            synth = _active;
        }

        if (synth != null)
        {
            try
            {
                synth.StopSpeakingAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Swallow — speech must never break typing flow
            }
        }
    }

    public async Task<bool> TrySpeakSsmlAsync(
        string ssml,
        SpeechSettings settings,
        ILoggingService? log,
        CancellationToken cancellationToken)
    {
        var (key, region) = ResolveCredentials(settings);
        if (key == null || region == null)
        {
            return false;
        }

        if (!IsNetworkAvailable())
        {
            return false;
        }

        SpeechSynthesizer? synthesizer = null;
        try
        {
            var config = SpeechConfig.FromSubscription(key, region);
            var voice = ResolveVoiceName(settings);
            config.SpeechSynthesisVoiceName = voice;

            synthesizer = new SpeechSynthesizer(config, audioConfig: null);

            lock (_gate)
            {
                _active = synthesizer;
            }

            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           _ = synthesizer.StopSpeakingAsync();
                       }
                       catch
                       {
                           // ignore
                       }
                   }))
            {
                var result = await synthesizer.SpeakSsmlAsync(ssml).ConfigureAwait(false);
                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    return true;
                }

                log?.Debug($"Azure speech finished with reason: {result.Reason}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            log?.Warning($"Azure speech failed (will fall back if allowed): {ex.Message}");
            return false;
        }
        finally
        {
            lock (_gate)
            {
                if (_active == synthesizer)
                {
                    _active = null;
                }
            }

            synthesizer?.Dispose();
        }
    }

    public static string ResolveVoiceName(SpeechSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.VoiceName))
        {
            return settings.VoiceName.Trim();
        }

        return "en-US-JennyNeural";
    }

    public static string BuildSsml(string text, SpeechSettings settings, double rateMultiplier)
    {
        var voice = ResolveVoiceName(settings);
        var ratePercent = (int)Math.Round((rateMultiplier - 1.0) * 100);
        var rateStr = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";
        var pitch = string.IsNullOrWhiteSpace(settings.OnlinePitch) ? "0%" : settings.OnlinePitch.Trim();
        var volume = string.IsNullOrWhiteSpace(settings.OnlineVolumeProsody)
            ? "default"
            : settings.OnlineVolumeProsody.Trim();

        var escaped = System.Security.SecurityElement.Escape(text) ?? string.Empty;

        return $"""
                <speak version='1.0' xml:lang='en-US'>
                  <voice name='{voice}'>
                    <prosody rate='{rateStr}' pitch='{pitch}' volume='{volume}'>
                      {escaped}
                    </prosody>
                  </voice>
                </speak>
                """;
    }
}
