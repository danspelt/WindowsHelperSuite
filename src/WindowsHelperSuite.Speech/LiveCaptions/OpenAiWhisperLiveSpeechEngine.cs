using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// OpenAI Whisper-based continuous speech recognition for Live Captions.
/// Captures microphone audio in rolling chunks and transcribes via the
/// OpenAI audio/transcriptions API ( Whisper ). Excellent accuracy for
/// diverse speech patterns including dysarthria / cerebral palsy.
/// </summary>
internal sealed class OpenAiWhisperLiveSpeechEngine : ILiveSpeechService, IDisposable
{
    private readonly Func<LiveCaptionSettings> _getSettings;
    private readonly ILoggingService? _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private CancellationTokenSource? _runCts;
    private Task? _captureTask;
    private WasapiCapture? _capture;

    // Rolling audio buffer for silence detection + chunking
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();
    private DateTime _lastSpeechDetected = DateTime.UtcNow;
    private bool _speechActive;
    private string _pendingPartial = string.Empty;

    public event EventHandler<string>? PartialTextReceived;
    public event EventHandler<string>? FinalTextReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ListeningStateChanged;

    public string ActiveEngineName => "OpenAI Whisper";
    public bool IsListening { get; private set; }

    public OpenAiWhisperLiveSpeechEngine(Func<LiveCaptionSettings> getSettings, ILoggingService? log = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _log = log;
    }

    private static string BuildEffectivePrompt(LiveCaptionSettings settings)
    {
        static IEnumerable<string> SplitLines(string? s) =>
            (s ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var basePrompt = settings.OpenAiWhisperPrompt?.Trim() ?? string.Empty;
        var hints = SplitLines(settings.OpenAiWhisperPhraseHints).Take(80).ToList();
        var examples = SplitLines(settings.OpenAiWhisperExampleSentences).Take(40).ToList();

        if (string.IsNullOrWhiteSpace(basePrompt) && hints.Count == 0 && examples.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(basePrompt))
        {
            sb.AppendLine(basePrompt);
        }

        if (hints.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Vocabulary / phrase hints (likely words and names):");
            foreach (var h in hints)
                sb.AppendLine($"- {h}");
        }

        if (examples.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Example sentences (speaker style):");
            foreach (var ex in examples)
                sb.AppendLine($"- {ex}");
        }

        // Keep prompts reasonably sized (helps latency + avoids request issues).
        const int maxChars = 1200;
        var text = sb.ToString().Trim();
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    public static string? ResolveApiKey(LiveCaptionSettings settings)
    {
        var k = settings.OpenAiApiKey?.Trim();
        if (!string.IsNullOrEmpty(k)) return k;
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    }

    public bool IsConfigured()
    {
        var key = ResolveApiKey(_getSettings());
        return !string.IsNullOrEmpty(key);
    }

    public async Task StartAsync(string languageTag = "en-US", CancellationToken cancellationToken = default)
    {
        if (IsListening)
            return;

        var settings = _getSettings();
        var key = ResolveApiKey(settings);
        if (string.IsNullOrEmpty(key))
        {
            ErrorOccurred?.Invoke(this, "OpenAI API key not configured. Add it in Settings or set OPENAI_API_KEY environment variable.");
            return;
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _runCts.Token;

        try
        {
            // Use loopback-capture-friendly format: 16 kHz, 16-bit, mono (Whisper preferred)
            _capture = new WasapiCapture();
            _capture.WaveFormat = new WaveFormat(16000, 16, 1);
            _capture.DataAvailable += OnCaptureDataAvailable;
            _capture.RecordingStopped += OnCaptureStopped;

            lock (_bufferLock)
            {
                _audioBuffer.Clear();
                _speechActive = false;
                _pendingPartial = string.Empty;
            }

            _captureTask = Task.Run(() => CaptureLoopAsync(token), token);
            _capture.StartRecording();

            IsListening = true;
            ListeningStateChanged?.Invoke(this, true);
            _log?.Information("Live Captions (OpenAI Whisper) started");
        }
        catch (Exception ex)
        {
            _log?.Warning($"Live Captions (Whisper) failed to start: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to start Whisper engine: {ex.Message}");
            await StopAsync(token).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsListening && _capture == null)
            return;

        _runCts?.Cancel();

        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnCaptureDataAvailable;
                _capture.RecordingStopped -= OnCaptureStopped;
                _capture.StopRecording();
            }
        }
        catch { /* ignore */ }

        if (_captureTask != null)
        {
            try { await _captureTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); } catch { /* ignore */ }
        }

        lock (_bufferLock)
        {
            _audioBuffer.Clear();
        }

        _capture?.Dispose();
        _capture = null;
        _runCts?.Dispose();
        _runCts = null;

        if (IsListening)
        {
            IsListening = false;
            ListeningStateChanged?.Invoke(this, false);
        }

        _log?.Debug("Live Captions (OpenAI Whisper) stopped");
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.Buffer == null || e.BytesRecorded <= 0)
            return;

        var bytes = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, bytes, 0, e.BytesRecorded);

        lock (_bufferLock)
        {
            _audioBuffer.AddRange(bytes);

            // Simple VAD: if audio level above threshold, mark speech active
            var level = CalculateRms(bytes);
            const double threshold = 0.02; // 2% of max amplitude
            if (level > threshold)
            {
                _speechActive = true;
                _lastSpeechDetected = DateTime.UtcNow;
            }
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _log?.Warning($"Whisper capture stopped with error: {e.Exception.Message}");
            ErrorOccurred?.Invoke(this, $"Microphone error: {e.Exception.Message}");
        }
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        // Process audio in ~2-second windows (Whisper works best with 5-30s chunks,
        // but for Live Captions we trade latency for accuracy by using smaller chunks)
        const int chunkMs = 2000;
        const int silenceMs = 800; // ms of silence to trigger finalization

        var lastProcessed = DateTime.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);

                byte[]? chunkToProcess = null;
                bool isFinal = false;

                lock (_bufferLock)
                {
                    var elapsed = DateTime.UtcNow - lastProcessed;
                    var silence = DateTime.UtcNow - _lastSpeechDetected;

                    // Minimum chunk size: 1 second of audio at 16kHz 16-bit mono = 32000 bytes
                    const int minBytes = 16000 * 2 * 1; // 1 second

                    if (_audioBuffer.Count >= minBytes && (elapsed.TotalMilliseconds >= chunkMs || silence.TotalMilliseconds >= silenceMs))
                    {
                        chunkToProcess = _audioBuffer.ToArray();
                        _audioBuffer.Clear();
                        lastProcessed = DateTime.UtcNow;
                        isFinal = silence.TotalMilliseconds >= silenceMs && _speechActive;
                        if (isFinal)
                            _speechActive = false;
                    }
                }

                if (chunkToProcess != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessAudioChunkAsync(chunkToProcess, isFinal, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _log?.Debug($"Whisper chunk processing error: {ex.Message}");
                        }
                    }, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _log?.Warning($"Whisper capture loop error: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Recognition error: {ex.Message}");
        }
    }

    private async Task ProcessAudioChunkAsync(byte[] pcm16Audio, bool isFinal, CancellationToken ct)
    {
        var settings = _getSettings();
        var key = ResolveApiKey(settings);
        if (string.IsNullOrEmpty(key))
            return;

        static string NormalizeLanguage(string? tag)
        {
            var t = (tag ?? "").Trim();
            if (t.Length == 0) return "en";
            // OpenAI STT expects ISO 639-1 like "en" (not BCP-47 like "en-US").
            var dash = t.IndexOf('-');
            if (dash > 0) t = t[..dash];
            return t.Length == 0 ? "en" : t.ToLowerInvariant();
        }

        // Convert PCM to WAV in memory (Whisper API accepts audio files)
        byte[] wavBytes;
        using (var ms = new MemoryStream())
        {
            using (var writer = new WaveFileWriter(ms, new WaveFormat(16000, 16, 1)))
            {
                await writer.WriteAsync(pcm16Audio, ct).ConfigureAwait(false);
            }
            wavBytes = ms.ToArray();
        }

        // Build multipart/form-data request
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(settings.OpenAiWhisperModel?.Trim() ?? "whisper-1"), "model");
        content.Add(new StringContent(NormalizeLanguage(settings.RecognitionLanguage)), "language");
        // "text" is the simplest + most compatible response format.
        content.Add(new StringContent("text"), "response_format");

        // Prompt helps guide recognition for specific speech patterns
        var prompt = BuildEffectivePrompt(settings);
        if (!string.IsNullOrEmpty(prompt))
            content.Add(new StringContent(prompt), "prompt");

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "chunk.wav");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = content;

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Debug($"Whisper API request failed: {ex.Message}");
            return;
        }

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _log?.Warning($"Whisper API error: {(int)res.StatusCode} {err}");
            var msg = res.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Whisper authentication failed (401). Check your OpenAI API key.",
                System.Net.HttpStatusCode.Forbidden => "Whisper access denied (403). Your API key may lack permissions.",
                System.Net.HttpStatusCode.TooManyRequests => "Whisper is rate-limiting requests (429). Please wait a moment and try again.",
                _ => $"Whisper request failed ({(int)res.StatusCode})."
            };
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? text = null;
        try
        {
            // When response_format=text, the response body is the transcript itself.
            text = json.Trim();
        }
        catch { /* ignore parse errors */ }

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (isFinal)
        {
            FinalTextReceived?.Invoke(this, text);
            lock (_bufferLock)
            {
                _pendingPartial = string.Empty;
            }
        }
        else
        {
            // For partial results, we show them but may revise as more audio comes
            lock (_bufferLock)
            {
                _pendingPartial = text;
            }
            PartialTextReceived?.Invoke(this, text);
        }
    }

    private static double CalculateRms(byte[] pcm16)
    {
        double sum = 0;
        int samples = pcm16.Length / 2;
        if (samples == 0) return 0;

        for (int i = 0; i < pcm16.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(pcm16, i);
            double normalized = sample / 32768.0;
            sum += normalized * normalized;
        }

        return Math.Sqrt(sum / samples);
    }

    public void Dispose()
    {
        try { _ = StopAsync(); } catch { /* ignore */ }
        _http.Dispose();
    }
}
