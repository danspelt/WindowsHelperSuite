using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSpace.Counseling;
using StillSpace.Services;
using StillSpace.Writer;

namespace StillSpace.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly StillSpaceSettingsStore _settingsStore = new();
    private readonly CorrectionMemoryStore _corrections = new();
    private readonly OpenAiCounselorClient _openAi = new();
    private readonly WriterAssistantClient _writer = new();
    private readonly AudioDeviceProbe _probe = new();
    private readonly TtsPlaybackService _tts = new();
    private readonly WindowsSpeechLiveService _winSpeech = new();
    private readonly SpeechDictationService _legacySpeech = new();
    private OpenAiRealtimeVoiceSession? _realtimeSession;
    private CancellationTokenSource? _realtimeCts;
    private DateTimeOffset? _tSpeechStarted;
    private DateTimeOffset? _tSpeechStopped;
    private DateTimeOffset? _tBufferCommitted;
    private DateTimeOffset? _tResponseCreated;
    private DateTimeOffset? _tFirstOutputAudio;

    private StillSpaceSettings _settings;
    private CancellationTokenSource? _ttsCts;
    private CancellationTokenSource? _speechCts;
    private CancellationTokenSource? _phraseHintCts;
    private bool _spaceHeld;

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        History = new ObservableCollection<ChatLine>();
    }

    public StillSpaceSettings Settings => _settings;

    public StillSpaceSettings CloneSettingsForEditor()
    {
        var json = JsonSerializer.Serialize(_settings);
        return JsonSerializer.Deserialize<StillSpaceSettings>(json) ?? new StillSpaceSettings();
    }

    public void ApplySettings(StillSpaceSettings s)
    {
        _settings = s;
        _settingsStore.Save(_settings);
        OnPropertyChanged(nameof(HeadsetOnlyMode));
        OnPropertyChanged(nameof(SttLang));
        OnPropertyChanged(nameof(VoiceStartOk));
        OnPropertyChanged(nameof(ShowRealtimeDiagnosticsStrip));
        OnPropertyChanged(nameof(ShowAiPhraseHint));
        if (!_settings.AiDictationNextWordHints)
            Ui(() => AiPhraseHint = "");
        RefreshHeadsetState();
    }

    public int CorrectionCount => _corrections.Load().Count;

    public ObservableCollection<ChatLine> History { get; }

    [ObservableProperty] private bool _sessionStarted;
    [ObservableProperty] private bool _textOnlySession;
    [ObservableProperty] private CounselingMode _sessionMode = CounselingMode.Support;
    [ObservableProperty] private string _draftUserText = "";
    [ObservableProperty] private string _liveTranscript = "";
    [ObservableProperty] private bool _listening;
    [ObservableProperty] private string _lastAssistant = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _crisisVisible;
    [ObservableProperty] private bool _speaking;
    [ObservableProperty] private bool _headsetConnected;
    [ObservableProperty] private string _headsetLabel = "—";
    [ObservableProperty] private bool _canRouteTts = true;
    [ObservableProperty] private bool _realtimeLiveActive;
    [ObservableProperty] private string _realtimeTurnStateLabel = "—";
    [ObservableProperty] private string _realtimeTurnTimingDetail = "";
    [ObservableProperty] private string _aiPhraseHint = "";

    public bool ShowWelcome => !SessionStarted;
    public bool ShowSession => SessionStarted;

    public bool ShowRealtimeHints => RealtimeLiveActive;

    public bool ShowRealtimeDiagnosticsStrip =>
        RealtimeLiveActive && _settings.ShowRealtimeVoiceDiagnostics;

    /// <summary>Gray writing-assistant continuation under dictation—not counselor replies; hidden during live voice.</summary>
    public bool ShowAiPhraseHint =>
        !RealtimeLiveActive
        && !TextOnlySession
        && _settings.AiDictationNextWordHints
        && !string.IsNullOrWhiteSpace(AiPhraseHint);

    public bool TextSendEnabled => !RealtimeLiveActive;

    public bool ShowStartLiveVoiceInSession =>
        SessionStarted && !RealtimeLiveActive && !TextOnlySession && VoiceStartOk;

    public string HeadsetStatusText => HeadsetConnected ? "Connected" : "Disconnected";

    public bool ShowHeadsetDisconnectBanner =>
        SessionStarted && _settings.HeadsetOnlyMode && !TextOnlySession && !HeadsetConnected;

    public bool MicToggleEnabled => VoiceLiveOk && !Busy && !RealtimeLiveActive;

    public bool HeadsetOnlyMode => _settings.HeadsetOnlyMode;
    public string SttLang => _settings.SttLang;

    public bool VoiceStartOk => !_settings.HeadsetOnlyMode || HeadsetConnected;

    /// <summary>Draft box plus live dictation line so “Send” and corrections see the full sentence, not draft-only.</summary>
    public string CombinedUserInputForSend()
    {
        var d = DraftUserText.Trim();
        var live = LiveTranscript.Trim();
        if (live.Length == 0) return d;
        if (d.Length == 0) return live;
        if (string.Equals(live, d, StringComparison.Ordinal)) return live;
        if (live.StartsWith(d + " ", StringComparison.Ordinal)) return live;
        return $"{d} {live}".Trim();
    }

    public string CorrectionMistakenBasis => CombinedUserInputForSend().Trim();

    public bool VoiceLiveOk => !TextOnlySession && (!_settings.HeadsetOnlyMode || HeadsetConnected);

    public bool VoiceOutOk =>
        !TextOnlySession
        && (!_settings.HeadsetOnlyMode || (HeadsetConnected && CanRouteTts));

    public void RefreshHeadsetState()
    {
        var match = string.IsNullOrWhiteSpace(_settings.HeadsetNameMatch) ? "OpenRun" : _settings.HeadsetNameMatch.Trim();
        var id = string.IsNullOrWhiteSpace(_settings.PreferredOutputDeviceId) ? null : _settings.PreferredOutputDeviceId.Trim();
        var device = _probe.FindHeadsetPlayback(match, id);

        if (!_settings.HeadsetOnlyMode)
        {
            CanRouteTts = true;
            HeadsetConnected = true;
            try
            {
                using var d = _probe.GetDefaultRender();
                HeadsetLabel = d.FriendlyName;
            }
            catch
            {
                HeadsetLabel = "—";
            }

            return;
        }

        if (device != null)
        {
            HeadsetLabel = device.FriendlyName;
            HeadsetConnected = true;
            CanRouteTts = true;
        }
        else
        {
            HeadsetLabel = "—";
            HeadsetConnected = false;
            CanRouteTts = false;
        }

        OnPropertyChanged(nameof(VoiceStartOk));
        NotifySessionUi();
    }

    private void NotifySessionUi()
    {
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ShowSession));
        OnPropertyChanged(nameof(ShowHeadsetDisconnectBanner));
        OnPropertyChanged(nameof(MicToggleEnabled));
        OnPropertyChanged(nameof(HeadsetStatusText));
        OnPropertyChanged(nameof(VoiceLiveOk));
        OnPropertyChanged(nameof(VoiceOutOk));
        OnPropertyChanged(nameof(ShowStartLiveVoiceInSession));
        OnPropertyChanged(nameof(ShowRealtimeDiagnosticsStrip));
        OnPropertyChanged(nameof(ShowAiPhraseHint));
        ReplayTtsCommand.NotifyCanExecuteChanged();
        ToggleMicCommand.NotifyCanExecuteChanged();
    }

    private void ResetRealtimeLifecycleUi()
    {
        _tSpeechStarted = null;
        _tSpeechStopped = null;
        _tBufferCommitted = null;
        _tResponseCreated = null;
        _tFirstOutputAudio = null;
        RealtimeTurnStateLabel = "—";
        RealtimeTurnTimingDetail = "";
    }

    private void OnRealtimeLifecycle(RealtimeLifecycleEvent e)
    {
        var utc = e.Utc;
        if (_settings.LogRealtimeTurnTimings)
            Trace.WriteLine($"[StillSpace Realtime] phase={e.Phase} utc={utc:O}");

        switch (e.Phase)
        {
            case RealtimeLifecyclePhase.SpeechStarted:
                _tSpeechStarted = utc;
                _tSpeechStopped = null;
                _tBufferCommitted = null;
                _tResponseCreated = null;
                _tFirstOutputAudio = null;
                break;
            case RealtimeLifecyclePhase.SpeechStopped:
                _tSpeechStopped = utc;
                break;
            case RealtimeLifecyclePhase.BufferCommitted:
                _tBufferCommitted = utc;
                break;
            case RealtimeLifecyclePhase.ResponseCreated:
                _tResponseCreated = utc;
                _tFirstOutputAudio = null;
                break;
            case RealtimeLifecyclePhase.FirstOutputAudio:
                _tFirstOutputAudio = utc;
                break;
        }

        if (_settings.LogRealtimeTurnTimings)
        {
            var detail = BuildLifecycleTimingLine(e.Phase, utc);
            if (detail.Length > 0)
                Trace.WriteLine($"[StillSpace Realtime]   {detail}");
        }

        if (!_settings.ShowRealtimeVoiceDiagnostics)
            return;

        Ui(() =>
        {
            RealtimeTurnStateLabel = MapLifecyclePhaseToLabel(e.Phase);
            RealtimeTurnTimingDetail = BuildLifecycleTimingLine(e.Phase, utc);
        });
    }

    private static string MapLifecyclePhaseToLabel(string phase) => phase switch
    {
        RealtimeLifecyclePhase.Listening => "Listening",
        RealtimeLifecyclePhase.SpeechStarted => "Speech detected",
        RealtimeLifecyclePhase.SpeechStopped => "End of turn (silence)",
        RealtimeLifecyclePhase.BufferCommitted => "Committing turn",
        RealtimeLifecyclePhase.ResponseCreated => "Generating reply",
        RealtimeLifecyclePhase.FirstOutputAudio => "Speaking (counselor)",
        RealtimeLifecyclePhase.OutputAudioDone => "Reply audio finished",
        _ => phase
    };

    private string BuildLifecycleTimingLine(string phase, DateTimeOffset utc) =>
        phase switch
        {
            RealtimeLifecyclePhase.SpeechStopped when _tSpeechStarted is { } t0 =>
                $"+{(utc - t0).TotalMilliseconds:F0} ms after speech started",
            RealtimeLifecyclePhase.BufferCommitted when _tSpeechStopped is { } t0 =>
                $"+{(utc - t0).TotalMilliseconds:F0} ms after end-of-turn",
            RealtimeLifecyclePhase.ResponseCreated when _tBufferCommitted is { } t0 =>
                $"+{(utc - t0).TotalMilliseconds:F0} ms after commit",
            RealtimeLifecyclePhase.FirstOutputAudio when _tResponseCreated is { } t0 =>
                $"+{(utc - t0).TotalMilliseconds:F0} ms after reply started",
            RealtimeLifecyclePhase.OutputAudioDone when _tFirstOutputAudio is { } t0 =>
                $"+{(utc - t0).TotalMilliseconds:F0} ms after first counselor audio",
            _ => ""
        };

    private void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a();
        else d.Invoke(a);
    }

    [RelayCommand]
    private void StartVoiceSession()
    {
        if (_settings.HeadsetOnlyMode && !HeadsetConnected) return;
        History.Clear();
        LastAssistant = "";
        DraftUserText = "";
        LiveTranscript = "";
        TextOnlySession = false;
        CrisisVisible = false;
        SessionStarted = true;
        _ = AutoStartContinuousVoiceAsync();
    }

    /// <summary>
    /// Voice sessions keep input active: OpenAI Realtime (mic always streamed) when an API key exists,
    /// otherwise Windows continuous dictation. Avoids needing a separate “start mic” step.
    /// </summary>
    private async Task AutoStartContinuousVoiceAsync()
    {
        try
        {
            await Task.Yield();
            if (!SessionStarted || TextOnlySession) return;
            if (_settings.HeadsetOnlyMode && !HeadsetConnected) return;
            if (RealtimeLiveActive || Busy) return;

            if (!string.IsNullOrEmpty(_openAi.ResolveApiKey(_settings)))
            {
                await StartRealtimeLiveCoreAsync().ConfigureAwait(true);
                if (!RealtimeLiveActive && VoiceLiveOk && !Busy)
                    StartSpeechContinuous();
            }
            else if (VoiceLiveOk)
            {
                StartSpeechContinuous();
            }
        }
        catch (Exception ex)
        {
            Ui(() => LastAssistant = $"Microphone: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRealtimeLive))]
    private async Task StartRealtimeLiveAsync()
    {
        if (_settings.HeadsetOnlyMode && !HeadsetConnected) return;
        if (!SessionStarted)
        {
            History.Clear();
            LastAssistant = "";
            DraftUserText = "";
            LiveTranscript = "";
            TextOnlySession = false;
            CrisisVisible = false;
            SessionStarted = true;
        }
        else if (TextOnlySession)
        {
            return;
        }

        await StartRealtimeLiveCoreAsync().ConfigureAwait(true);
    }

    private bool CanStartRealtimeLive() =>
        VoiceStartOk && !RealtimeLiveActive && !Busy && (!SessionStarted || !TextOnlySession);

    [RelayCommand(CanExecute = nameof(CanStopRealtimeLive))]
    private async Task StopRealtimeLiveAsync() => await StopRealtimeLiveCoreAsync().ConfigureAwait(true);

    private bool CanStopRealtimeLive() => RealtimeLiveActive;

    private async Task StartRealtimeLiveCoreAsync()
    {
        var key = _openAi.ResolveApiKey(_settings);
        if (string.IsNullOrEmpty(key))
        {
            LastAssistant = "Missing OpenAI API key for Realtime.";
            return;
        }

        StopSpeech();
        try
        {
            _realtimeCts?.Cancel();
            _realtimeCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _realtimeCts = new CancellationTokenSource();

        try
        {
            await (_realtimeSession?.StopAsync() ?? Task.CompletedTask).ConfigureAwait(true);
        }
        catch
        {
            /* ignore */
        }

        _realtimeSession?.Dispose();
        _realtimeSession = new OpenAiRealtimeVoiceSession();

        NAudio.CoreAudioApi.MMDevice? playbackDevice = null;
        var match = string.IsNullOrWhiteSpace(_settings.HeadsetNameMatch) ? "OpenRun" : _settings.HeadsetNameMatch.Trim();
        if (_settings.HeadsetOnlyMode)
        {
            playbackDevice = _probe.FindHeadsetPlayback(
                match,
                string.IsNullOrWhiteSpace(_settings.PreferredOutputDeviceId) ? null : _settings.PreferredOutputDeviceId.Trim());
            if (playbackDevice == null)
            {
                LastAssistant = "Headset not found — connect OpenRun (or match Settings) for live voice in private mode.";
                _realtimeSession.Dispose();
                _realtimeSession = null;
                return;
            }
        }

        NAudio.CoreAudioApi.MMDevice? captureDevice = null;
        var inputId = string.IsNullOrWhiteSpace(_settings.PreferredInputDeviceId)
            ? null
            : _settings.PreferredInputDeviceId.Trim();
        if (inputId != null)
            captureDevice = _probe.FindCaptureDevice(inputId, null);
        if (captureDevice == null && _settings.HeadsetOnlyMode)
            captureDevice = _probe.FindCaptureDevice(null, match);

        Busy = true;
        try
        {
            await _realtimeSession
                .StartAsync(
                    key,
                    _settings,
                    SessionMode,
                    captureDevice,
                    playbackDevice,
                    _settings.HeadsetOnlyMode,
                    onSessionReady: () => { },
                    onInputTranscriptDelta: d => Ui(() => LiveTranscript += d),
                    onInputTranscriptReset: () => Ui(() => LiveTranscript = ""),
                    onOutputTranscriptReset: () => Ui(() => LastAssistant = ""),
                    onOutputTranscriptDelta: d => Ui(() => LastAssistant += d),
                    onSpeechStarted: () => Ui(() => Speaking = false),
                    onAssistantAudioStarted: () => Ui(() => Speaking = true),
                    onAssistantAudioDone: () => Ui(() => Speaking = false),
                    onError: err => Ui(() =>
                        LastAssistant = err.Contains("Missing", StringComparison.Ordinal)
                            ? err
                            : $"(Realtime.) {err}"),
                    onLifecycle: OnRealtimeLifecycle,
                    _realtimeCts.Token)
                .ConfigureAwait(true);
            RealtimeLiveActive = true;
            Listening = true;
        }
        catch (Exception ex)
        {
            LastAssistant = $"(Realtime.) {ex.Message}";
            try
            {
                await _realtimeSession.StopAsync().ConfigureAwait(true);
            }
            catch
            {
                /* ignore */
            }

            _realtimeSession.Dispose();
            _realtimeSession = null;
        }
        finally
        {
            Busy = false;
        }

        StartRealtimeLiveCommand.NotifyCanExecuteChanged();
        StopRealtimeLiveCommand.NotifyCanExecuteChanged();
    }

    private async Task StopRealtimeLiveCoreAsync()
    {
        if (_realtimeSession == null)
        {
            RealtimeLiveActive = false;
            Listening = false;
            Speaking = false;
            ResetRealtimeLifecycleUi();
            return;
        }

        try
        {
            _realtimeCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            await _realtimeSession.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            /* ignore */
        }

        _realtimeSession.Dispose();
        _realtimeSession = null;

        try
        {
            _realtimeCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _realtimeCts = null;
        RealtimeLiveActive = false;
        Listening = false;
        Speaking = false;
        LiveTranscript = "";
        ResetRealtimeLifecycleUi();
        StartRealtimeLiveCommand.NotifyCanExecuteChanged();
        StopRealtimeLiveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void StartTextOnlySession()
    {
        History.Clear();
        LastAssistant = "";
        DraftUserText = "";
        LiveTranscript = "";
        TextOnlySession = true;
        CrisisVisible = false;
        StopSpeech();
        SessionStarted = true;
    }

    [RelayCommand]
    private async Task EndSession()
    {
        await StopRealtimeLiveCoreAsync().ConfigureAwait(true);
        StopSpeech();
        _tts.RequestStop();
        SessionStarted = false;
        TextOnlySession = false;
        Listening = false;
        LiveTranscript = "";
        DraftUserText = "";
    }

    [RelayCommand]
    private void RetryDevices() => RefreshHeadsetState();

    [RelayCommand(CanExecute = nameof(CanToggleMic))]
    private void ToggleMic()
    {
        if (Listening) StopSpeech();
        else StartSpeechContinuous();
    }

    private bool CanToggleMic() => MicToggleEnabled;

    public void PushToTalkDown()
    {
        if (RealtimeLiveActive || !SessionStarted || Busy || !VoiceLiveOk || _spaceHeld) return;
        _spaceHeld = true;
        StartSpeechContinuous();
    }

    public void PushToTalkUp()
    {
        if (!_spaceHeld) return;
        _spaceHeld = false;
        StopSpeech();
    }

    private void StartSpeechContinuous()
    {
        if (TextOnlySession || Busy) return;
        if (_settings.HeadsetOnlyMode && !HeadsetConnected) return;

        CancelPhraseHintScheduler();
        AiPhraseHint = "";
        Listening = true;
        LiveTranscript = "";
        try
        {
            _speechCts?.Cancel();
            _speechCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _speechCts = new CancellationTokenSource();
        _ = StartSpeechInternalAsync(_speechCts.Token);
    }

    private async Task StartSpeechInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (WindowsSpeechLiveService.IsSupported)
            {
                var ok = await _winSpeech
                    .StartAsync(
                        _settings.SttLang,
                        h => Ui(() => ApplySpeechHypothesisToLive(h)),
                        f => Ui(() =>
                        {
                            var merged = _corrections.ApplyGlossary(f.Trim());
                            if (merged.Length > 0)
                                DraftUserText = string.IsNullOrWhiteSpace(DraftUserText)
                                    ? merged
                                    : $"{DraftUserText} {merged}".Trim();
                            LiveTranscript = "";
                            ScheduleAiPhraseHintRefresh();
                        }),
                        cancellationToken)
                    .ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();
                if (ok) return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_legacySpeech.IsAvailable)
            {
                Ui(() =>
                {
                    var hint = _winSpeech.LastStartFailureMessage;
                    LiveTranscript = string.IsNullOrWhiteSpace(hint)
                        ? "Speech recognition unavailable — use typing."
                        : hint;
                    Listening = false;
                });
                return;
            }

            _legacySpeech.Start(
                _settings.SttLang,
                h => Ui(() => ApplySpeechHypothesisToLive(h)),
                f => Ui(() =>
                {
                    var merged = _corrections.ApplyGlossary(f.Trim());
                    if (merged.Length > 0)
                        DraftUserText = string.IsNullOrWhiteSpace(DraftUserText)
                            ? merged
                            : $"{DraftUserText} {merged}".Trim();
                    LiveTranscript = "";
                    ScheduleAiPhraseHintRefresh();
                }));
        }
        catch (OperationCanceledException)
        {
            Ui(() => Listening = false);
        }
        catch (Exception ex)
        {
            Ui(() =>
            {
                LiveTranscript = ex.Message;
                Listening = false;
            });
        }
    }

    private void FlushPartialTranscriptToDraft()
    {
        var combined = CombinedUserInputForSend().Trim();
        if (combined.Length == 0) return;
        DraftUserText = _corrections.ApplyGlossary(combined).Trim();
        LiveTranscript = "";
        ScheduleAiPhraseHintRefresh();
    }

    /// <summary>Windows hypothesis is only the current phrase; prefix with committed draft/finals so the whole sentence stays visible.</summary>
    private void ApplySpeechHypothesisToLive(string hypothesis)
    {
        if (string.IsNullOrEmpty(hypothesis)) return;
        var d = DraftUserText.TrimEnd();
        LiveTranscript = d.Length == 0 ? hypothesis : $"{d} {hypothesis}".TrimEnd();
        ScheduleAiPhraseHintRefresh();
    }

    private void CancelPhraseHintScheduler()
    {
        try
        {
            _phraseHintCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        _phraseHintCts?.Dispose();
        _phraseHintCts = null;
    }

    private void ScheduleAiPhraseHintRefresh()
    {
        if (RealtimeLiveActive || TextOnlySession || !_settings.AiDictationNextWordHints)
        {
            Ui(() => AiPhraseHint = "");
            CancelPhraseHintScheduler();
            return;
        }

        if (string.IsNullOrEmpty(_writer.ResolveApiKey(_settings)))
        {
            Ui(() => AiPhraseHint = "");
            CancelPhraseHintScheduler();
            return;
        }

        CancelPhraseHintScheduler();
        _phraseHintCts = new CancellationTokenSource();
        var token = _phraseHintCts.Token;
        _ = RunAiPhraseHintAsync(token);
    }

    private async Task RunAiPhraseHintAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(WriterAssistantClient.DefaultDebounceMilliseconds, token).ConfigureAwait(false);
            var partial = await Application.Current!.Dispatcher
                .InvokeAsync(() => CombinedUserInputForSend().Trim())
                .Task.ConfigureAwait(false);
            if (partial.Length < 5 || partial.Length > 700)
            {
                await Application.Current!.Dispatcher.InvokeAsync(() => AiPhraseHint = "")
                    .Task.ConfigureAwait(false);
                return;
            }

            using var apiTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(WriterAssistantClient.DefaultRequestTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, apiTimeout.Token);
            var (ok, text, _) = await _writer.PredictNextWordsAsync(_settings, partial, linked.Token)
                .ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            var hint = ok && !string.IsNullOrWhiteSpace(text) ? text.Trim() : "";
            await Application.Current!.Dispatcher.InvokeAsync(() => AiPhraseHint = hint).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* replaced by newer schedule */
        }
        catch
        {
            try
            {
                await Application.Current!.Dispatcher.InvokeAsync(() => AiPhraseHint = "")
                    .Task.ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private void StopSpeech()
    {
        CancelPhraseHintScheduler();
        Ui(() => AiPhraseHint = "");
        try
        {
            _speechCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        _legacySpeech.Stop();
        try
        {
            _winSpeech.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }

        Ui(() =>
        {
            FlushPartialTranscriptToDraft();
            Listening = false;
        });
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (RealtimeLiveActive) return;
        var raw = CombinedUserInputForSend().Trim();
        var text = _corrections.ApplyGlossary(raw).Trim();
        if (text.Length == 0 || Busy) return;

        CancelPhraseHintScheduler();
        Ui(() => AiPhraseHint = "");

        CrisisVisible = CrisisDetector.Detect(text) == CrisisLevel.Elevated;

        var userPayload = !string.IsNullOrWhiteSpace(_settings.PreferredName)
            ? $"The user’s preferred name is “{_settings.PreferredName.Trim()}”. Their message:\n\n{text}"
            : text;

        Busy = true;
        try
        {
            if (_settings.PauseBeforeReplyMs > 0)
                await Task.Delay(_settings.PauseBeforeReplyMs).ConfigureAwait(true);

            var systems = CounselorPrompts.BuildSystemMessages(SessionMode);
            var messages = new List<ChatMessage>();
            messages.AddRange(systems);
            foreach (var turn in History)
            {
                var role = turn.RoleLabel == "You" ? "user" : "assistant";
                messages.Add(new ChatMessage(role, turn.Content));
            }

            messages.Add(new ChatMessage("user", userPayload));

            var (ok, reply, err) = await _openAi.CompleteAsync(_settings, messages).ConfigureAwait(true);
            if (!ok)
            {
                LastAssistant = err.StartsWith("Missing OpenAI API key", StringComparison.Ordinal)
                    ? err
                    : $"(Could not reach the AI service.) {err}";
                return;
            }

            History.Add(new ChatLine("You", text));
            History.Add(new ChatLine("Counselor", reply));
            DraftUserText = "";
            LiveTranscript = "";
            AiPhraseHint = "";
            LastAssistant = reply;

            if (_settings.AutoReadAloud && VoiceOutOk && _settings.PreferOpenAiTts)
                await PlayReplyAsync(reply).ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplayTts))]
    private async Task ReplayTtsAsync()
    {
        if (string.IsNullOrWhiteSpace(LastAssistant)) return;
        await PlayReplyAsync(LastAssistant).ConfigureAwait(true);
    }

    private bool CanReplayTts() =>
        !RealtimeLiveActive
        && !string.IsNullOrWhiteSpace(LastAssistant)
        && VoiceOutOk
        && !Speaking
        && !Busy;

    private async Task PlayReplyAsync(string text)
    {
        if (TextOnlySession || string.IsNullOrWhiteSpace(text)) return;

        if (_settings.HeadsetOnlyMode)
        {
            var device = _probe.FindHeadsetPlayback(
                string.IsNullOrWhiteSpace(_settings.HeadsetNameMatch) ? "OpenRun" : _settings.HeadsetNameMatch.Trim(),
                string.IsNullOrWhiteSpace(_settings.PreferredOutputDeviceId) ? null : _settings.PreferredOutputDeviceId.Trim());
            if (device == null) return;

            Speaking = true;
            _tts.ResetStop();
            _ttsCts?.Cancel();
            _ttsCts = new CancellationTokenSource();
            try
            {
                var (ok, audio, err) = await _openAi.TextToSpeechAsync(_settings, text, _ttsCts.Token)
                    .ConfigureAwait(true);
                if (ok && audio != null)
                    await _tts.PlayMp3Async(audio, device, _ttsCts.Token).ConfigureAwait(true);
            }
            finally
            {
                Speaking = false;
            }

            return;
        }

        Speaking = true;
        _tts.ResetStop();
        _ttsCts?.Cancel();
        _ttsCts = new CancellationTokenSource();
        try
        {
            var (ok, audio, err) = await _openAi.TextToSpeechAsync(_settings, text, _ttsCts.Token)
                .ConfigureAwait(true);
            if (ok && audio != null)
            {
                await _tts.PlayMp3Async(audio, device: null, _ttsCts.Token).ConfigureAwait(true);
            }
            else
            {
                await Task.Run(() =>
                {
                    using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                    synth.Speak(text);
                }, _ttsCts.Token).ConfigureAwait(true);
            }
        }
        finally
        {
            Speaking = false;
        }
    }

    public void SaveCorrection(string mistaken, string corrected, string? context = null)
    {
        _corrections.Save(mistaken, corrected, context);
        OnPropertyChanged(nameof(CorrectionCount));
    }

    [RelayCommand]
    private void SwitchToTextOnlyFromBanner()
    {
        StopSpeech();
        TextOnlySession = true;
    }

    partial void OnSessionStartedChanged(bool value)
    {
        NotifySessionUi();
        StartRealtimeLiveCommand.NotifyCanExecuteChanged();
        StopRealtimeLiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextOnlySessionChanged(bool value) => NotifySessionUi();

    partial void OnHeadsetConnectedChanged(bool value) => NotifySessionUi();

    partial void OnBusyChanged(bool value)
    {
        NotifySessionUi();
        StartRealtimeLiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSpeakingChanged(bool value) => NotifySessionUi();

    partial void OnRealtimeLiveActiveChanged(bool value)
    {
        if (value)
        {
            CancelPhraseHintScheduler();
            Ui(() => AiPhraseHint = "");
        }

        NotifySessionUi();
        OnPropertyChanged(nameof(MicToggleEnabled));
        OnPropertyChanged(nameof(ShowRealtimeHints));
        OnPropertyChanged(nameof(ShowRealtimeDiagnosticsStrip));
        OnPropertyChanged(nameof(TextSendEnabled));
        OnPropertyChanged(nameof(ShowStartLiveVoiceInSession));
        ToggleMicCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
        ReplayTtsCommand.NotifyCanExecuteChanged();
        StartRealtimeLiveCommand.NotifyCanExecuteChanged();
        StopRealtimeLiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnListeningChanged(bool value)
    {
        OnPropertyChanged(nameof(MicToggleEnabled));
        ToggleMicCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastAssistantChanged(string value) => ReplayTtsCommand.NotifyCanExecuteChanged();

    partial void OnAiPhraseHintChanged(string value) => OnPropertyChanged(nameof(ShowAiPhraseHint));

    partial void OnDraftUserTextChanged(string value) => ScheduleAiPhraseHintRefresh();

    public void Dispose()
    {
        StopSpeech();
        try
        {
            _speechCts?.Cancel();
            _speechCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _realtimeCts?.Cancel();
            _realtimeCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _realtimeSession?.Dispose();
        _realtimeSession = null;

        _tts.RequestStop();
        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _openAi.Dispose();
        _writer.Dispose();
        _legacySpeech.Dispose();
        CancelPhraseHintScheduler();
    }
}
