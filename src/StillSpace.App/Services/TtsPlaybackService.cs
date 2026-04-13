using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StillSpace.Services;

public sealed class TtsPlaybackService
{
    private readonly object _gate = new();
    private bool _stopRequested;

    public void RequestStop() => _stopRequested = true;

    public void ResetStop() => _stopRequested = false;

    /// <summary>
    /// Plays MP3 bytes. When <paramref name="device"/> is null, uses the default multimedia render endpoint.
    /// </summary>
    public Task PlayMp3Async(byte[] mp3, MMDevice? device, CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"stillspace-tts-{Guid.NewGuid():N}.mp3");
        return Task.Run(() =>
        {
            lock (_gate)
            {
                _stopRequested = false;
                try
                {
                    File.WriteAllBytes(temp, mp3);
                    cancellationToken.ThrowIfCancellationRequested();

                    using var reader = new AudioFileReader(temp);
                    using WasapiOut output = device != null
                        ? new WasapiOut(device, AudioClientShareMode.Shared, false, 200)
                        : new WasapiOut(AudioClientShareMode.Shared, 200);

                    output.Init(reader);
                    output.Play();
                    while (output.PlaybackState == PlaybackState.Playing && !cancellationToken.IsCancellationRequested && !_stopRequested)
                        Thread.Sleep(80);
                }
                finally
                {
                    try { File.Delete(temp); } catch { /* ignore */ }
                }
            }
        }, cancellationToken);
    }
}
