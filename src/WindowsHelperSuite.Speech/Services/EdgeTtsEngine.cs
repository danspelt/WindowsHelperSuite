using System.IO;
using edge_tts_net;
using NAudio.Wave;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Free neural TTS via Microsoft Edge's online speech service.
/// No API key required — uses the same high-quality neural voices
/// as Edge browser's "Read Aloud" feature.
/// </summary>
internal sealed class EdgeTtsEngine : IDisposable
{
    private const string DefaultVoice = "en-US-AvaMultilingualNeural";

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private IWavePlayer? _activePlayer;
    private bool _disposed;

    /// <summary>
    /// Speaks the given text using Edge neural TTS with real-time NAudio playback.
    /// Returns true on success, false on any failure.
    /// </summary>
    public async Task<bool> TrySpeakAsync(
        string text,
        SpeechSettings settings,
        double rateMultiplier,
        float volume,
        ILoggingService? log,
        CancellationToken externalToken)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
            return false;

        CancellationTokenSource? linkedCts = null;
        try
        {
            var cts = new CancellationTokenSource();
            lock (_gate) { _cts = cts; }
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, externalToken);

            var voiceName = ResolveEdgeVoice(settings);
            var ratePercent = (int)Math.Round((rateMultiplier - 1.0) * 100);
            var rateStr = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";
            var volPercent = (int)Math.Round((volume - 1.0f) * 100);
            var volStr = volPercent >= 0 ? $"+{volPercent}%" : $"{volPercent}%";

            var option = new TTSOption(
                voice: voiceName,
                rate: rateStr,
                volume: volStr,
                pitch: "+0Hz"
            );

            var edgeTts = new edge_tts_net.EdgeTTSNet();

            // Collect audio chunks into a memory stream
            using var audioStream = new MemoryStream();
            await edgeTts.TTS(text, metaObj =>
            {
                if (metaObj.Type == edge_tts_net.TTSMetadataType.Audio && metaObj.Data != null)
                {
                    audioStream.Write(metaObj.Data);
                }
            }, option).ConfigureAwait(false);

            if (linkedCts.IsCancellationRequested)
                return false;

            if (audioStream.Length == 0)
            {
                log?.Debug("Edge TTS returned empty audio.");
                return false;
            }

            // Play the collected audio via NAudio
            audioStream.Position = 0;
            using var mp3Reader = new Mp3FileReader(audioStream);
            using var waveOut = new WaveOutEvent();

            lock (_gate) { _activePlayer = waveOut; }

            waveOut.Init(mp3Reader);
            waveOut.Volume = Math.Clamp(volume, 0f, 1f);

            var tcs = new TaskCompletionSource<bool>();
            waveOut.PlaybackStopped += (_, args) =>
            {
                tcs.TrySetResult(args.Exception == null);
            };

            waveOut.Play();

            // Wait for playback to finish or cancellation
            using (linkedCts.Token.Register(() =>
            {
                waveOut.Stop();
                tcs.TrySetResult(false);
            }))
            {
                await tcs.Task.ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            log?.Debug($"Edge TTS failed: {ex.Message}");
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _activePlayer = null;
                _cts = null;
            }
            linkedCts?.Dispose();
        }
    }

    public void StopSpeaking()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _activePlayer?.Stop(); } catch { /* ignore */ }
        }
    }

    public static string ResolveEdgeVoice(SpeechSettings settings)
    {
        // Use the same VoiceName setting — Edge voices use the same naming scheme as Azure
        if (!string.IsNullOrWhiteSpace(settings.VoiceName))
        {
            var name = settings.VoiceName.Trim();
            // Edge voice names don't always end with "Neural" — accept as-is
            return name;
        }

        return DefaultVoice;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSpeaking();
    }
}
