using System.Collections.Concurrent;
using System.Net.Http;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.AI.Providers;
using WindowsHelperSuite.AI.Services;
using WindowsHelperSuite.App;
using WindowsHelperSuite.App.Services.Writer;
using WindowsHelperSuite.App.ViewModels;
using WindowsHelperSuite.App.Views;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Core.Modes;
using WindowsHelperSuite.Core.Modules.Text;
using WindowsHelperSuite.Infrastructure.Audio;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Infrastructure.Services;
using WindowsHelperSuite.Hotkeys.Services;
using WindowsHelperSuite.Overlay.Services;
using WindowsHelperSuite.Input.Services;
using WindowsHelperSuite.Prediction.Services;
using WindowsHelperSuite.Speech.LiveCaptions;
using WindowsHelperSuite.Speech.Services;
using VoiceBridge.Contracts;
using WindowsHelperSuite.Writer.Llm;
using WindowsHelperSuite.VoiceBridge;

namespace WindowsHelperSuite.App.Services;

public class ApplicationService : IDisposable
{
    /// <summary>Collapses runs of spaces/tabs/NBSP that models often emit (e.g. after a word).</summary>
    private static readonly Regex HorizontalWhitespaceRun = new("[ \\t\\u00A0]{2,}", RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    // private readonly TrayIconService _trayIconService; // Removed - Writer is main window
    private readonly HotkeyService _hotkeyService;
    private readonly OverlayService _overlayService;
    private readonly InputService _inputService;
    private readonly IPredictionService _predictionService;
    private readonly IWriterContext _writerContext;
    private readonly ManualWriterPhaseContext? _manualPhaseContext;
    private readonly ILearningEngine _learningEngine;
    private readonly ITypingModel _typingModel;
    private readonly ISpeechService _speechService;
    private readonly IModeManager _modeManager;
    private readonly Queue<string> _recentWords = new();
    private readonly System.Timers.Timer _focusCheckTimer;
    /// <summary>Cap for phrase/word-bank context list only; prediction uses the full sentence from InputService.</summary>
    private const int MaxPhraseContextWords = 4096;
#pragma warning disable CS0414
    private int _focusLostCount = 0;
#pragma warning restore CS0414
    private int _mouseDismissAnchorX = int.MinValue;
    private int _mouseDismissAnchorY = int.MinValue;
    private const int MouseDismissMoveThresholdSq = 6400; // 80px movement
    private const long MouseDismissGraceMs = 2000; // ignore mouse-dismiss for 2s after showing
    private long _overlayShownTick;
    private SpeakMode _speakMode = SpeakMode.Both;
    private readonly object _writerStateLock = new();
    // Writer starts asleep — must be woken with the user-configured WakeWriter hotkey.
    // Overlay dismiss (Esc / explicit) puts it back to sleep.
    private volatile bool _writerAwake = false;
    private readonly IAiVocabularyGateService _aiVocabGate;
    private readonly ConcurrentDictionary<string, Task<bool>> _vocabGateTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWriterVocabularyRemoteStore _remoteVocabulary;
    private readonly IChatService _chatService;
    private readonly IAiSuggestionService _aiSuggestionService;
    private readonly IConversationStore _conversationStore;
    private readonly ChatOptions _chatOptions;
    private ChatWindow? _chatWindow;
    private LiveCaptionsWindow? _liveCaptionsWindow;
    private CancellationTokenSource? _highlightSpeechCts;
    private VoiceBridgeListener? _voiceBridgeListener;

    public ApplicationService(
        IWriterContext? writerContext = null,
        ILearningEngine? learningEngine = null,
        ITypingModel? typingModel = null)
    {
        _loggingService = new LoggingService();
        _settingsService = new SettingsService();
        _settingsService.Load();
        _settingsService.Settings.MongoVocabulary ??= new MongoVocabularySettings();
        _settingsService.Settings.VoiceBridge ??= new VoiceBridgeSettings();

        _remoteVocabulary = WriterVocabularyMongoStore.Create(_settingsService.Settings.MongoVocabulary, _loggingService);
        if (_remoteVocabulary.IsEnabled)
        {
            var mv = _settingsService.Settings.MongoVocabulary;
            _loggingService.Information(
                $"Mongo vocabulary sync on → {mv.DatabaseName}.{mv.CollectionName}");
        }

        _aiVocabGate = new AiVocabularyGateService(_loggingService, () => _settingsService.Settings.Ai);

        // AI Chat services
        _chatOptions = LoadChatOptions();
        var chatProvider = new OpenAiCompatibleChatProvider(_chatOptions, _loggingService);
        _chatService = new ChatService(chatProvider, _loggingService);
        _conversationStore = new JsonConversationStore(_loggingService);
        _aiSuggestionService = new AiSuggestionService(_loggingService, BuildOverlayAiSettings);

        // Tray icon removed - Writer is now the main window
        // _trayIconService = new TrayIconService(_loggingService, _settingsService, ReloadHotkeys, OpenChat, OpenLiveCaptions);
        _hotkeyService = new HotkeyService(_loggingService);
        _overlayService = new OverlayService(_loggingService, _settingsService);
        // _trayIconService.OverlayService = _overlayService;
        _inputService = new InputService(
            _loggingService,
            new CachingSecretFieldDetector(new SecretFieldDetector()),
            new CachingWriterOverlayExclusionDetector(new WriterOverlayExclusionDetector()));
        _typingModel = typingModel ?? new TypingModelService();
        if (writerContext != null)
        {
            _writerContext = writerContext;
            _manualPhaseContext = null;
        }
        else
        {
            _manualPhaseContext = new ManualWriterPhaseContext(
                new CachingWriterContext(new ForegroundWriterContext()));
            _writerContext = _manualPhaseContext;
        }

        _predictionService = new CompositePredictionService(
            _typingModel,
            () => _settingsService.Settings.Writer,
            () => _writerContext.GetSnapshot());
        _learningEngine = learningEngine ?? new DefaultLearningEngine();
        _speechService = new SpeechService(() => _settingsService.Settings.Speech, _loggingService);

        // Apply speech settings
        ApplySpeechSettings();

        // Periodic focus check - hide overlay when no text field is focused
        _focusCheckTimer = new System.Timers.Timer(500);
        _focusCheckTimer.Elapsed += OnFocusCheckTimerElapsed;
        _focusCheckTimer.AutoReset = true;

        _modeManager = new ModeManager(_settingsService, _loggingService, ApplyApplicationMode);
        _modeManager.Initialize();
        // _trayIconService.ApplyModeIndicator(_modeManager.CurrentMode);
        // _modeManager.ModeChanged += (_, mode) => _trayIconService.ApplyModeIndicator(mode);

        _inputService.SecretFieldProtectionChanged += (_, isProtected) =>
            DeferWriterUi(() => OnSecretFieldProtectionChanged(_, isProtected));

        // Wire up suggestion selection to text injection
        _overlayService.SuggestionSelected += OnSuggestionSelected;
        _overlayService.SuggestionHighlightChanged += OnSuggestionHighlightChanged;
        _overlayService.CloseRequested += OnOverlayCloseRequested;
        _inputService.TryGetHighlightedSuggestionSlot = () => _overlayService.GetHighlightedSuggestionSlot();

        WireInputToOverlay();
        RegisterHotkeyActions();
        RegisterDefaultHotkeys();

        if (_settingsService.Settings.Writer.StartAwake)
        {
            _writerAwake = true;
            _loggingService.Information("Writer starting awake (StartAwake enabled)");
        }

        // Install the writer hook first, then hotkeys, so the hotkey LL hook runs before Input (last-installed = first in chain).
        _inputService.Start();
        _hotkeyService.Start();

        // Writer starts hidden - only shows with wake key (`)
        _loggingService.Information("Writer started hidden - press F9 to wake");

        TryStartVoiceBridgeListener();

        _loggingService.Information("Application started (v4 - enhanced injection + key suppression)");
    }

    private void TryStartVoiceBridgeListener()
    {
        var vb = _settingsService.Settings.VoiceBridge;
        if (!vb.EnableListener)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vb.SharedToken))
        {
            vb.SharedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _settingsService.Save();
            _loggingService.Information(
                "Voice Bridge: generated voiceBridge.sharedToken in settings.json — use it as the WebSocket query token on the phone.");
        }

        _voiceBridgeListener = new VoiceBridgeListener(_loggingService, () =>
        {
            var s = _settingsService.Settings.VoiceBridge;
            return new VoiceBridgeConnectionOptions
            {
                Enabled = s.EnableListener,
                Port = Math.Clamp(s.ListenPort, 1024, 65535),
                ListenOnAllInterfaces = s.ListenOnAllInterfaces,
                SharedToken = s.SharedToken
            };
        });
        _voiceBridgeListener.MessageReceived += OnVoiceBridgeMessageReceived;
        _voiceBridgeListener.ConnectionChanged += OnVoiceBridgeConnectionChanged;
        _voiceBridgeListener.Start();
    }

    private void OnVoiceBridgeConnectionChanged(bool connected, string? sessionId, string? deviceId)
    {
        _loggingService.Information(
            $"Voice Bridge {(connected ? "connected" : "disconnected")}: session={sessionId} deviceId={deviceId}");
    }

    private void OnVoiceBridgeMessageReceived(VoiceBridgeEnvelope env)
    {
        _loggingService.Debug(
            $"Voice Bridge message: type={env.Type} session={env.SessionId} textLen={env.Text?.Length ?? 0}");

        if (string.Equals(env.Type, VoiceBridgeMessageTypes.AudioChunk, StringComparison.OrdinalIgnoreCase))
        {
            var bytes = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(env.AudioBase64))
                {
                    bytes = Convert.FromBase64String(env.AudioBase64).Length;
                }
            }
            catch
            {
                bytes = 0;
            }

            _loggingService.Information(
                $"Voice Bridge audio: seq={env.Seq} format={env.AudioFormat} sr={env.SampleRate} ch={env.Channels} bytes={bytes}");
        }
    }

    public void Run()
    {
        _loggingService.Information("Application running in Writer-only mode (no system tray)");
    }

    public void RequestShowSettings()
    {
        _loggingService.Information("Activation requested (show settings) - no settings UI in Writer-only mode");
        // Settings UI not available in Writer-only mode
    }

    /// <summary>
    /// Keyboard hook runs on a dedicated thread; overlay and prediction must run on the WPF dispatcher
    /// so we never block the hook with WPF/UIAutomation work (that caused crashes while typing).
    /// </summary>
    private void DeferWriterUi(Action action)
    {
        void Wrapped()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"Writer UI: {ex.Message}");
            }
        }

        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null)
        {
            Wrapped();
            return;
        }

        if (d.CheckAccess())
        {
            Wrapped();
            return;
        }

        d.BeginInvoke(DispatcherPriority.Normal, Wrapped);
    }

    private void WireInputToOverlay()
    {
        _inputService.TypingStarted += (_, _) =>
        {
            if (!_writerAwake) return;
            _hasValidTextInput = true;
            DeferWriterUi(ShowOverlay);
        };

        _inputService.TextCaptured += (_, text) =>
        {
            if (!_writerAwake) return;
            try
            {
                _speechService.NotifyKeystroke();
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"NotifyKeystroke: {ex.Message}");
            }

            DeferWriterUi(() => UpdateSuggestions(text));
        };

        _inputService.WordTyped += (_, e) =>
        {
            if (!_writerAwake) return;
            try
            {
                var finalWord = ApplyWordCapitalizationInjectionIfNeeded(e);
                DeferWriterUi(() =>
                {
                    OnWordTypedDeferred(finalWord, e);
                    _hasValidTextInput = true;
                    _inputService.IsOverlayVisible = true;
                    _focusCheckTimer.Start();
                    UpdateSuggestions(string.Empty);
                });
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"WordTyped handler error: {ex.Message}");
            }
        };

        _inputService.PasteIntercept += OnPasteIntercept;
        _inputService.SentenceTyped += (_, sentence) =>
        {
            if (!_writerAwake) return;
            DeferWriterUi(() => OnSentenceTyped(sentence));
        };
        _inputService.OverlayDismissRequested += (_, e) => DeferWriterUi(() =>
        {
            // Only Esc actually dismisses. SessionEnded (sentence done, inactivity,
            // non-writer field) is ignored — overlay stays until mouse-move or Esc.
            if (e.Reason == OverlayDismissReason.Soft)
            {
                _speechService.Stop();
                HideOverlay();
                lock (_writerStateLock)
                {
                    _currentWord = string.Empty;
                    _currentSuggestions = [];
                }

                _hasValidTextInput = false;
                _writerAwake = false;
                _loggingService.Information("Writer hidden via Esc — press ` to wake");
                NotifyWriterSleepTray();
                return;
            }

            _loggingService.Debug($"Dismiss request ignored ({e.Reason}) — overlay stays (mouse/Esc/Enter only)");
        });

        _inputService.TypingStopped += (_, _) => DeferWriterUi(() =>
        {
            lock (_writerStateLock)
            {
                _currentWord = string.Empty;
            }

            _loggingService.Debug("Typing stopped - keeping overlay visible");
        });

        _inputService.InvalidTypingDetected += (_, _) => DeferWriterUi(() =>
        {
            _loggingService.Debug("InvalidTypingDetected - overlay stays visible (dismissed only by mouse/Esc)");
        });

        _inputService.SelectionKeyPressed += (_, slot) =>
        {
            var now = Environment.TickCount64;
            if (now - _lastSelectionTick < 300)
            {
                _loggingService.Debug($"Selection key {slot}: ignored (cooldown)");
                return;
            }

            SuggestionItem? suggestion;
            int charsToDelete;
            string typedPartial;
            lock (_writerStateLock)
            {
                suggestion = _currentSuggestions.FirstOrDefault(x => x.Slot == slot);
                if (suggestion == null)
                {
                    _loggingService.Debug($"Selection key {slot}: no suggestion found");
                    return;
                }

                charsToDelete = _currentWord?.Length ?? 0;
                typedPartial = _currentWord ?? string.Empty;
                _lastSelectionTick = now;
                _currentWord = string.Empty;
                
                // Mark this suggestion as used
                if (suggestion != null)
                {
                    var textToAdd = suggestion.InsertText ?? suggestion.DisplayText ?? string.Empty;
                    if (!string.IsNullOrEmpty(textToAdd))
                    {
                        _usedSuggestionsInCurrentSession.Add(textToAdd);
                    }
                }
                
                _currentSuggestions = [];
            }

            _inputService.ResetAfterInsertion();

            if (suggestion == null) return;

            var rawBeforePartial = _inputService.GetRawTextBeforeCurrentPartial();
            var capOpts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
            var acceptedText = CapitalizationService.FixInsertion(rawBeforePartial, suggestion.DisplayText ?? string.Empty, capOpts);
            var textToInsert = ResolveSuggestionInsertText(suggestion, acceptedText);
            var suggestionKind = suggestion.Kind;

            _loggingService.Debug(
                $"Selection key {slot}: suggestion='{suggestion.DisplayText}', acceptedText='{acceptedText}', textToInsert='{textToInsert}', charsToDelete={charsToDelete}");

            DeferWriterUi(() =>
            {
                _overlayService.FlashSelection(slot);
                _inputService.BeginInjection();
                try
                {
                    if (charsToDelete > 0)
                    {
                        Win32TextInjection.SendBackspace(charsToDelete);
                        Thread.Sleep(30);
                    }

                    var insertWithSpace = NormalizeSuggestionInsertText(rawBeforePartial, textToInsert) + " ";
                    Win32TextInjection.SendText(insertWithSpace);
                    _inputService.ApplySuggestionInsertion(charsToDelete, insertWithSpace);
                    LearnAcceptedSuggestion(acceptedText, typedPartial, suggestionKind, charsToDelete);
                    _loggingService.Information($"Inserted: {acceptedText}");

                    if (_settingsService.Settings.Speech.EnableSpeechOnSelection &&
                        (_speakMode == SpeakMode.WordsOnly || _speakMode == SpeakMode.Both))
                    {
                        _speechService.SpeakQueued(acceptedText, true);
                        _overlayService.ShowSpeakerIndicator(acceptedText);
                    }

                    _hasValidTextInput = true;
                    UpdateSuggestions(string.Empty);
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"Text injection failed for \"{acceptedText}\": {ex.Message}");
                }
                finally
                {
                    _inputService.EndInjection();
                }
            });
        };

        _inputService.NextPageKeyPressed += (_, _) => DeferWriterUi(() => _overlayService.MoveToNextPage());
        _inputService.PreviousPageKeyPressed += (_, _) => DeferWriterUi(() => _overlayService.MoveToPreviousPage());
        _inputService.SuggestionHighlightMoved += (_, delta) =>
            DeferWriterUi(() => _overlayService.MoveSuggestionHighlight(delta));
        _inputService.OverlayLayoutToggleRequested += (_, _) => DeferWriterUi(() =>
        {
            if (!_writerAwake || !_inputService.IsOverlayVisible)
            {
                return;
            }

            _overlayService.ToggleHorizontalVerticalLayout();
        });
    }

    private string _currentWord = string.Empty;
    private List<SuggestionItem> _currentSuggestions = [];
    private HashSet<string> _usedSuggestionsInCurrentSession = [];
    private bool _hasValidTextInput = false;
    private long _lastSelectionTick;
    private int _lastOverlayCaretX = int.MinValue;
    private int _lastOverlayCaretY = int.MinValue;
    private int _overlayAiGeneration;
    private CancellationTokenSource? _overlayAiDebounceCts;
    private readonly HttpClient _localOverlayHttp = new();

    private void ShowOverlay()
    {
        if (_inputService.IsInProtectedField)
        {
            return;
        }

        _hasValidTextInput = true;
        _inputService.IsOverlayVisible = true;
        CaptureMouseDismissAnchor();
        UpdateSuggestions(_inputService.GetCurrentWord());
        _focusCheckTimer.Start();
        _loggingService.Debug("Overlay shown - typing started");
    }

    private void HideOverlay()
    {
        _loggingService.Debug($"HideOverlay called from: {new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name}");
        _focusCheckTimer.Stop();
        _overlayService.HideSuggestions();
        _inputService.IsOverlayVisible = false;
        ResetMouseDismissTracking();
    }

    private void HideOverlayFromMouseMove()
    {
        if (!_inputService.IsOverlayVisible)
        {
            return;
        }

        _loggingService.Debug("HideOverlayFromMouseMove called");
        _overlayService.HideSuggestions();
        _inputService.IsOverlayVisible = false;
        ResetMouseDismissTracking();
    }

    /// <summary>Show overlay after wake when there is no caret yet (desktop / non-text focus).</summary>
    private void ShowWriterAwaitingTextField()
    {
        _hasValidTextInput = true;
        _inputService.IsOverlayVisible = true;
        CaptureMouseDismissAnchor();
        _focusCheckTimer.Start();

        List<SuggestionItem> suggestions;
        lock (_writerStateLock)
        {
            _currentWord = string.Empty;
            suggestions = _predictionService.GetSuggestions(string.Empty, string.Empty, _writerContext.GetSnapshot()).ToList();
            _currentSuggestions = suggestions;
        }

        if (suggestions.Count > 0)
        {
            _overlayService.ShowSuggestions(suggestions);
        }
        else
        {
            // No suggestions yet — force the window visible so it doesn't instantly hide
            _overlayService.ShowWindow();
        }

        _overlayService.SetOverlayStatusHint("Focus a text field and type — suggestions will follow the caret");
        _overlayService.RepositionAtCaret();
        _loggingService.Debug("Writer awake — awaiting text field focus");
    }

    private void CaptureMouseDismissAnchor()
    {
        _overlayShownTick = Environment.TickCount64;
        if (Win32Cursor.TryGetPosition(out var x, out var y))
        {
            _mouseDismissAnchorX = x;
            _mouseDismissAnchorY = y;
        }
    }

    private void ResetMouseDismissTracking()
    {
        _mouseDismissAnchorX = int.MinValue;
        _mouseDismissAnchorY = int.MinValue;
    }

    private void TryDismissOverlayOnMouseMove()
    {
        if (!_writerAwake || !_inputService.IsOverlayVisible)
        {
            return;
        }

        // Grace period after showing — don't dismiss from minor jitter right after wake
        if (Environment.TickCount64 - _overlayShownTick < MouseDismissGraceMs)
        {
            return;
        }

        if (!Win32Cursor.TryGetPosition(out var x, out var y))
        {
            return;
        }

        if (_mouseDismissAnchorX == int.MinValue)
        {
            CaptureMouseDismissAnchor();
            return;
        }

        if (_overlayService.IsCursorOverOverlay())
        {
            CaptureMouseDismissAnchor();
            return;
        }

        var dx = x - _mouseDismissAnchorX;
        var dy = y - _mouseDismissAnchorY;
        if (dx * dx + dy * dy < MouseDismissMoveThresholdSq)
        {
            return;
        }

        _writerAwake = false;
        HideOverlayFromMouseMove();
        _loggingService.Information("Writer hidden — mouse moved (press F9 to wake)");
    }

    private void OnSecretFieldProtectionChanged(object? sender, bool isProtected)
    {
        if (isProtected)
        {
            _speechService.Stop();
            HideOverlay();
            lock (_writerStateLock)
            {
                _currentWord = string.Empty;
                _currentSuggestions = [];
            }

            _previousWord = string.Empty;
            _recentWords.Clear();
            _hasValidTextInput = false;
            _loggingService.Debug("Writer suppressed: protected field (no overlay, no learning, no speech)");
        }
        else
        {
            _previousWord = string.Empty;
            _recentWords.Clear();
            lock (_writerStateLock)
            {
                _currentWord = string.Empty;
                _currentSuggestions = [];
            }

            _hasValidTextInput = false;
            _loggingService.Debug("Writer: left protected field — fresh prediction state");
        }
    }

    private void OnFocusCheckTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            if (_writerAwake && _inputService.IsOverlayVisible)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(TryDismissOverlayOnMouseMove);
            }

            var hasCaret = Win32Caret.GetCaretPosition(out var caretX, out var caretY);
            var isValidCaret = hasCaret && (caretX != 0 || caretY != 0);
            if (!isValidCaret)
            {
                _focusLostCount = 0; // Overlay stays - only dismissed by mouse move or Esc
            }
            else
            {
                _focusLostCount = 0;
                if (_writerAwake
                    && _inputService.IsOverlayVisible
                    && Win32Caret.GetCaretPosition(out var cx, out var cy)
                    && (cx != _lastOverlayCaretX || cy != _lastOverlayCaretY))
                {
                    _lastOverlayCaretX = cx;
                    _lastOverlayCaretY = cy;
                    DeferWriterUi(() => _overlayService.RepositionAtCaret());
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Focus check error: {ex.Message}");
        }
    }

    private void UpdateSuggestions(string text)
    {
        if (_inputService.IsInProtectedField)
        {
            return;
        }

        // Only show overlay if we have valid text input
        if (!_hasValidTextInput)
        {
            _loggingService.Debug("UpdateSuggestions called but no valid text input, skipping");
            return;
        }

        _overlayService.SetOverlayStatusHint(null);

        var context = _inputService.GetSuggestionContextPrefix();

        List<SuggestionItem> suggestions;
        lock (_writerStateLock)
        {
            _currentWord = text;
            suggestions = _predictionService.GetSuggestions(context, text, _writerContext.GetSnapshot()).ToList();
            _currentSuggestions = suggestions;
        }

        // Always recapture mouse anchor when showing — ensures grace period resets
        CaptureMouseDismissAnchor();

        _overlayService.ShowSuggestions(suggestions);
        _inputService.IsOverlayVisible = true;
        if (!_focusCheckTimer.Enabled)
        {
            _focusCheckTimer.Start();
        }

        if (_settingsService.Settings.Writer.FollowCaret)
        {
            _overlayService.RepositionAtCaret();
            if (Win32Caret.GetCaretPosition(out var cx, out var cy))
            {
                _lastOverlayCaretX = cx;
                _lastOverlayCaretY = cy;
            }
        }

        // Dreamlike: preview the most likely suggestion before user accepts it
        PreviewTopSuggestion(suggestions);

        ScheduleOverlayAiEnrichment();
    }

    private AiSettings BuildOverlayAiSettings()
    {
        var ai = _settingsService.Settings.Ai;
        var model = string.IsNullOrWhiteSpace(ai.OverlayAiSuggestionModel) ? ai.Model : ai.OverlayAiSuggestionModel.Trim();
        return new AiSettings
        {
            EnableAiSuggestions = ai.EnableOverlayAiSuggestions,
            EnableAiPhraseCompletion = ai.EnableOverlayAiSuggestions,
            ApiKey = ai.ApiKey,
            Model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model,
            ApiBaseUrl = string.IsNullOrWhiteSpace(ai.ApiBaseUrl) ? "https://api.openai.com/v1" : ai.ApiBaseUrl.Trim().TrimEnd('/'),
            MaxAiSuggestions = Math.Clamp(ai.OverlayAiMaxSuggestions, 1, 9),
            AiTimeoutMs = Math.Clamp(ai.OverlayAiTimeoutMs, 500, 30_000)
        };
    }

    private void ScheduleOverlayAiEnrichment()
    {
        var aiWriter = _settingsService.Settings.Ai;
        if (!aiWriter.EnableOverlayAiSuggestions)
        {
            return;
        }

        _overlayAiDebounceCts?.Cancel();
        _overlayAiDebounceCts?.Dispose();
        _overlayAiDebounceCts = new CancellationTokenSource();
        var debounceToken = _overlayAiDebounceCts.Token;
        var gen = ++_overlayAiGeneration;

        if (!string.IsNullOrWhiteSpace(aiWriter.ApiKey))
        {
            _ = Task.Run(() => RunOverlayAiEnrichmentAsync(gen, debounceToken));
            return;
        }

        if (aiWriter.EnableOverlayLocalLlm)
        {
            _ = Task.Run(() => RunLocalLlmOverlayEnrichmentAsync(gen, debounceToken));
        }
    }

    private async Task RunOverlayAiEnrichmentAsync(int gen, CancellationToken debounceToken)
    {
        try
        {
            var debounceMs = Math.Clamp(_settingsService.Settings.Ai.OverlayAiDebounceMs, 0, 2000);
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs, debounceToken).ConfigureAwait(false);
            }

            if (debounceToken.IsCancellationRequested || gen != _overlayAiGeneration)
            {
                return;
            }

            var aiWriter = _settingsService.Settings.Ai;
            if (!aiWriter.EnableOverlayAiSuggestions || string.IsNullOrWhiteSpace(aiWriter.ApiKey))
            {
                return;
            }

            var context = _inputService.GetSuggestionContextPrefix();
            var word = _inputService.GetCurrentWord();
            var fullSentence = Win32Caret.TryGetTextForOverlayContext(out var liveFieldText)
                ? liveFieldText
                : _inputService.GetFullSentenceForOverlay();

            var lineForAi = string.IsNullOrWhiteSpace(fullSentence)
                ? (string.IsNullOrWhiteSpace(context) ? word : $"{context} {word}".Trim())
                : fullSentence;

            var requestFingerprint = context + "\u001f" + word + "\u001f" + lineForAi;

            var maxAi = Math.Clamp(aiWriter.OverlayAiMaxSuggestions, 1, 9);
            var prevWord = context.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            var request = new AiSuggestionRequest
            {
                CurrentText = lineForAi,
                CurrentWord = word,
                PreviousCompletedWord = string.IsNullOrWhiteSpace(word) ? prevWord : null,
                MaxSuggestions = maxAi
            };

            using var apiTimeout = new CancellationTokenSource(aiWriter.OverlayAiTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(debounceToken, apiTimeout.Token);

            var aiResults = await _aiSuggestionService.GetPhraseSuggestionsAsync(request, linked.Token).ConfigureAwait(false);

            if (gen != _overlayAiGeneration || aiResults.Count == 0)
            {
                return;
            }

            DeferWriterUi(() =>
            {
                if (gen != _overlayAiGeneration || !_hasValidTextInput)
                {
                    return;
                }

                var ctxNow = _inputService.GetSuggestionContextPrefix();
                var wNow = _inputService.GetCurrentWord();
                var fullNow = Win32Caret.TryGetTextForOverlayContext(out var liveNow)
                    ? liveNow
                    : _inputService.GetFullSentenceForOverlay();
                var lineNow = string.IsNullOrWhiteSpace(fullNow)
                    ? (string.IsNullOrWhiteSpace(ctxNow) ? wNow : $"{ctxNow} {wNow}".Trim())
                    : fullNow;
                var fpNow = ctxNow + "\u001f" + wNow + "\u001f" + lineNow;

                if (!string.Equals(requestFingerprint, fpNow, StringComparison.Ordinal))
                {
                    return;
                }

                List<SuggestionItem> freshLocal;
                lock (_writerStateLock)
                {
                    freshLocal = _predictionService.GetSuggestions(ctxNow, wNow, _writerContext.GetSnapshot()).ToList();
                }

                var postSpace = string.IsNullOrWhiteSpace(wNow);
                var merged = MergeAiOverlaySuggestions(freshLocal, aiResults, maxAi, postSpace);
                lock (_writerStateLock)
                {
                    _currentSuggestions = merged;
                }

                _overlayService.SetOverlayStatusHint(null);
                _overlayService.ShowSuggestions(merged);
                if (_settingsService.Settings.Writer.FollowCaret)
                {
                    _overlayService.RepositionAtCaret();
                }
            });
        }
        catch (OperationCanceledException)
        {
            /* debounce or API timeout — no overlay hint (typing continues) */
        }
        catch (Exception ex)
        {
            _loggingService.Debug($"Overlay AI enrichment: {ex.Message}");
            DeferWriterUi(() =>
            {
                if (_hasValidTextInput &&
                    _settingsService.Settings.Ai.EnableOverlayAiSuggestions &&
                    !string.IsNullOrWhiteSpace(_settingsService.Settings.Ai.ApiKey))
                {
                    _overlayService.SetOverlayStatusHint("AI suggestions unavailable — using local word bank only.");
                }
            });
        }
    }

    private LocalLlmOptions BuildLocalOverlayLlmOptions()
    {
        var ai = _settingsService.Settings.Ai;
        var model = string.IsNullOrWhiteSpace(ai.OverlayLocalLlmModel)
            ? (string.IsNullOrWhiteSpace(ai.Model) ? "local-model" : ai.Model.Trim())
            : ai.OverlayLocalLlmModel.Trim();

        return new LocalLlmOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(ai.OverlayLocalLlmBaseUrl)
                ? "http://localhost:1234/v1"
                : ai.OverlayLocalLlmBaseUrl.Trim().TrimEnd('/'),
            Model = model,
            TimeoutMs = Math.Clamp(ai.OverlayAiTimeoutMs, 400, 10_000),
            MaxSuggestions = Math.Clamp(ai.OverlayAiMaxSuggestions, 1, 9)
        };
    }

    private async Task RunLocalLlmOverlayEnrichmentAsync(int gen, CancellationToken debounceToken)
    {
        try
        {
            var debounceMs = Math.Clamp(_settingsService.Settings.Ai.OverlayAiDebounceMs, 0, 2000);
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs, debounceToken).ConfigureAwait(false);
            }

            if (debounceToken.IsCancellationRequested || gen != _overlayAiGeneration)
            {
                return;
            }

            var aiWriter = _settingsService.Settings.Ai;
            if (!aiWriter.EnableOverlayAiSuggestions || !aiWriter.EnableOverlayLocalLlm)
            {
                return;
            }

            var context = _inputService.GetSuggestionContextPrefix();
            var word = _inputService.GetCurrentWord();
            var fullSentence = Win32Caret.TryGetTextForOverlayContext(out var liveFieldText)
                ? liveFieldText
                : _inputService.GetFullSentenceForOverlay();

            var lineForAi = string.IsNullOrWhiteSpace(fullSentence)
                ? (string.IsNullOrWhiteSpace(context) ? word : $"{context} {word}".Trim())
                : fullSentence;

            var requestFingerprint = context + "\u001f" + word + "\u001f" + lineForAi;

            var maxAi = Math.Clamp(aiWriter.OverlayAiMaxSuggestions, 1, 9);
            using var apiTimeout = new CancellationTokenSource(Math.Clamp(aiWriter.OverlayAiTimeoutMs, 400, 10_000));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(debounceToken, apiTimeout.Token);

            var options = BuildLocalOverlayLlmOptions();
            var lines = await OverlayLocalLlmEnrichment.FetchSuggestionLinesAsync(
                _localOverlayHttp,
                options,
                lineForAi,
                context,
                word,
                _writerContext.GetSnapshot(),
                linked.Token).ConfigureAwait(false);

            if (gen != _overlayAiGeneration || lines.Count == 0)
            {
                return;
            }

            var aiResults = lines.Select(t => new AiSuggestionResult { Text = t }).ToList();

            DeferWriterUi(() =>
            {
                if (gen != _overlayAiGeneration || !_hasValidTextInput)
                {
                    return;
                }

                var ctxNow = _inputService.GetSuggestionContextPrefix();
                var wNow = _inputService.GetCurrentWord();
                var fullNow = Win32Caret.TryGetTextForOverlayContext(out var liveNow)
                    ? liveNow
                    : _inputService.GetFullSentenceForOverlay();
                var lineNow = string.IsNullOrWhiteSpace(fullNow)
                    ? (string.IsNullOrWhiteSpace(ctxNow) ? wNow : $"{ctxNow} {wNow}".Trim())
                    : fullNow;
                var fpNow = ctxNow + "\u001f" + wNow + "\u001f" + lineNow;

                if (!string.Equals(requestFingerprint, fpNow, StringComparison.Ordinal))
                {
                    return;
                }

                List<SuggestionItem> freshLocal;
                lock (_writerStateLock)
                {
                    freshLocal = _predictionService.GetSuggestions(ctxNow, wNow, _writerContext.GetSnapshot()).ToList();
                }

                var postSpace = string.IsNullOrWhiteSpace(wNow);
                var merged = MergeAiOverlaySuggestions(freshLocal, aiResults, maxAi, postSpace);
                lock (_writerStateLock)
                {
                    _currentSuggestions = merged;
                }

                _overlayService.SetOverlayStatusHint(null);
                _overlayService.ShowSuggestions(merged);
                if (_settingsService.Settings.Writer.FollowCaret)
                {
                    _overlayService.RepositionAtCaret();
                }
            });
        }
        catch (OperationCanceledException)
        {
            /* debounce or timeout */
        }
        catch (Exception ex)
        {
            _loggingService.Debug($"Overlay local LLM: {ex.Message}");
        }
    }

    private static List<SuggestionItem> MergeAiOverlaySuggestions(
        IReadOnlyList<SuggestionItem> local,
        IReadOnlyList<AiSuggestionResult> aiResults,
        int maxAiSlots,
        bool postSpace = false)
    {
        const int postSpaceLocalQuota = 4;
        var maxSlots = 9;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<SuggestionItem>();

        static bool IsNextWordSlot(SuggestionItem s) =>
            s.Kind == SuggestionKind.NextWord
            || (!s.DisplayText.Contains(' ', StringComparison.Ordinal)
                && s.Kind is SuggestionKind.WordCompletion or SuggestionKind.UserHistory or SuggestionKind.NextWord);

        void AddLocal(IEnumerable<SuggestionItem> items)
        {
            foreach (var s in items)
            {
                if (merged.Count >= maxSlots)
                {
                    return;
                }

                var d = s.DisplayText.Trim();
                if (d.Length == 0 || seen.Contains(d))
                {
                    continue;
                }

                seen.Add(d);
                merged.Add(s);
            }
        }

        void AddAi()
        {
            var aiAdded = 0;
            foreach (var r in aiResults)
            {
                if (aiAdded >= maxAiSlots || merged.Count >= maxSlots)
                {
                    break;
                }

                var isSentence = aiAdded == 0;
                var t = isSentence
                    ? SanitizeAiSentenceText(r.Text)
                    : SanitizeOverlaySuggestionText(r.Text);
                if (t.Length == 0 || t.Length > 200 || seen.Contains(t))
                {
                    continue;
                }

                seen.Add(t);
                merged.Add(new SuggestionItem
                {
                    DisplayText = t,
                    InsertText = string.Empty,
                    Kind = isSentence ? SuggestionKind.AiSentence : SuggestionKind.AiSuggestion,
                    Score = isSentence ? 6000 : 5200 - aiAdded * 14
                });
                aiAdded++;
            }
        }

        if (postSpace)
        {
            AddLocal(local.Where(IsNextWordSlot).OrderByDescending(s => s.Score).Take(postSpaceLocalQuota));
            AddAi();
            AddLocal(local.Where(s => !IsNextWordSlot(s)).OrderByDescending(s => s.Score));
            AddLocal(local.Where(IsNextWordSlot).OrderByDescending(s => s.Score).Skip(postSpaceLocalQuota));
        }
        else
        {
            AddAi();
            AddLocal(local);
        }

        for (var i = 0; i < merged.Count; i++)
        {
            merged[i].Slot = i + 1;
        }

        return merged;
    }

    private void OnSuggestionHighlightChanged(object? sender, string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return;
        }

        if (!_settingsService.Settings.Speech.EnableSpeechOnHighlight ||
            (_speakMode != SpeakMode.WordsOnly && _speakMode != SpeakMode.Both))
        {
            return;
        }

        _highlightSpeechCts?.Cancel();
        _highlightSpeechCts?.Dispose();
        _highlightSpeechCts = new CancellationTokenSource();
        var token = _highlightSpeechCts.Token;
        var text = displayText.Trim();
        var debounceMs = Math.Clamp(_settingsService.Settings.Speech.HighlightSpeechDebounceMs, 0, 2000);

        _ = Task.Run(async () =>
        {
            try
            {
                if (debounceMs > 0)
                {
                    await Task.Delay(debounceMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            DeferWriterUi(() =>
            {
                if (!_settingsService.Settings.Speech.EnableSpeechOnHighlight ||
                    (_speakMode != SpeakMode.WordsOnly && _speakMode != SpeakMode.Both))
                {
                    return;
                }

                _speechService.SpeakQueued(text, true);
                _overlayService.ShowSpeakerIndicator(text);
            });
        });
    }

    /// <summary>
    /// User clicked the ✕ close button on the overlay. Put the writer to sleep so
    /// keystrokes no longer run through the prediction pipeline. Only the WakeWriter
    /// hotkey can wake it back up.
    /// </summary>
    private void OnOverlayCloseRequested(object? sender, EventArgs e)
    {
        DeferWriterUi(() =>
        {
            _speechService.Stop();
            HideOverlay();
            lock (_writerStateLock)
            {
                _currentWord = string.Empty;
                _currentSuggestions = [];
            }

            _hasValidTextInput = false;
            _previousWord = string.Empty;
            _recentWords.Clear();
            _writerAwake = false;
            _loggingService.Information("Writer put to sleep via close button — only WakeWriter hotkey will wake it");
            NotifyWriterSleepTray();
        });
    }

    private void OnSuggestionSelected(object? sender, int slot)
    {
        // Legacy path — kept for overlay click selection if ever added
        SuggestionItem? suggestion;
        int charsToDelete;
        string typedPartial;
        lock (_writerStateLock)
        {
            suggestion = _currentSuggestions.FirstOrDefault(s => s.Slot == slot);
            if (suggestion == null)
            {
                return;
            }

            charsToDelete = _currentWord?.Length ?? 0;
            typedPartial = _currentWord ?? string.Empty;
            _currentWord = string.Empty;
            
            // Mark this suggestion as used
            if (suggestion != null)
            {
                var textToAdd = suggestion.InsertText ?? suggestion.DisplayText ?? string.Empty;
                if (!string.IsNullOrEmpty(textToAdd))
                {
                    _usedSuggestionsInCurrentSession.Add(textToAdd);
                }
            }
            
            _currentSuggestions = [];
        }

        _inputService.ClearCurrentWord();

        if (suggestion == null) return;

        var rawBeforePartial = _inputService.GetRawTextBeforeCurrentPartial();
        var capOpts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
        var acceptedText = CapitalizationService.FixInsertion(rawBeforePartial, suggestion.DisplayText ?? string.Empty, capOpts);
        var textToInsert = ResolveSuggestionInsertText(suggestion, acceptedText);

        _inputService.BeginInjection();
        try
        {
            if (charsToDelete > 0)
            {
                Win32TextInjection.SendBackspace(charsToDelete);
            }

            var insertWithSpace = NormalizeSuggestionInsertText(rawBeforePartial, textToInsert) + " ";
            Win32TextInjection.SendText(insertWithSpace);
            _inputService.ApplySuggestionInsertion(charsToDelete, insertWithSpace);
            LearnAcceptedSuggestion(acceptedText, typedPartial, suggestion.Kind, charsToDelete);
            _loggingService.Information($"Inserted: {acceptedText}");
            UpdateSuggestions(string.Empty);
        }
        finally
        {
            _inputService.EndInjection();
        }
    }

    private string? _lastPreviewedText;

    /// <summary>
    /// Dreamlike feature: quietly preview the top suggestion before user accepts it.
    /// If the user accepts it, the normal speech will skip re-speaking (already heard it!)
    /// </summary>
    private void PreviewTopSuggestion(IReadOnlyList<SuggestionItem> suggestions)
    {
        if (suggestions.Count == 0)
            return;

        var top = suggestions[0];

        // Only preview high-confidence suggestions (score > 2500)
        if (top.Score < 2500)
            return;

        // Skip phrases - too long for preview, and user might not want them
        if (top.Kind == SuggestionKind.PhraseCompletion || top.DisplayText.Contains(' '))
            return;

        // Skip if we already previewed this exact word recently
        if (string.Equals(_lastPreviewedText, top.DisplayText, StringComparison.OrdinalIgnoreCase))
            return;

        // Only preview if speech is enabled for highlighting
        var speech = _settingsService.Settings.Speech;
        if (!speech.EnableSpeechOnHighlight)
            return;

        _lastPreviewedText = top.DisplayText;

        // Whisper-preview the suggestion (faster, quieter)
        _speechService.PreviewSuggestion(top.DisplayText);
    }

    private void RegisterHotkeyActions()
    {
        _hotkeyService.RegisterAction("VolumeUp", () =>
        {
            Win32Audio.VolumeUp();
            _loggingService.Information("Volume increased");
        });

        _hotkeyService.RegisterAction("VolumeDown", () =>
        {
            Win32Audio.VolumeDown();
            _loggingService.Information("Volume decreased");
        });

        _hotkeyService.RegisterAction("VolumeMute", () =>
        {
            Win32Audio.VolumeMute();
            _loggingService.Information("Volume muted/unmuted");
        });

        _hotkeyService.RegisterAction("WriterRefresh", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("WriterRefresh ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            _loggingService.Information("Writer refresh requested");
        });

        _hotkeyService.RegisterAction("ToggleOverlay", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("ToggleOverlay ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            DeferWriterUi(() =>
            {
                if (Win32Caret.GetCaretPosition(out var x, out var y) && (x != 0 || y != 0))
                {
                    ShowOverlay();
                }
                else
                {
                    _loggingService.Debug("ToggleOverlay hotkey pressed but no text input detected");
                }
            });
        });

        _hotkeyService.RegisterAction("PauseWriter", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("PauseWriter ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            _inputService.IsEnabled = !_inputService.IsEnabled;
            _loggingService.Information($"Writer {(_inputService.IsEnabled ? "enabled" : "paused")}");
        });

        _hotkeyService.RegisterAction("WakeWriter", () =>
        {
            DeferWriterUi(() =>
            {
                if (_inputService.IsInProtectedField)
                {
                    _loggingService.Debug("WakeWriter ignored — protected field focused");
                    return;
                }

                _overlayService.ClearSuppression();

                // Reset mouse-dismiss tracking BEFORE setting awake so grace period starts fresh
                _overlayShownTick = Environment.TickCount64;
                CaptureMouseDismissAnchor();

                var wasAsleep = !_writerAwake;
                _writerAwake = true;
                if (wasAsleep)
                {
                    _loggingService.Information($"Writer woken via {GetWakeWriterHotkeyDisplay()} hotkey");
                    NotifyWriterWakeTray();
                }

                // Always show overlay on wake key, even if already awake
                _inputService.IsOverlayVisible = true;
                _overlayService.ShowWindow();
                if (IsWriterCaretAvailable())
                {
                    ShowOverlay();
                }
                else
                {
                    ShowWriterAwaitingTextField();
                }
            });
        });

        _hotkeyService.RegisterAction("KillWriter", () =>
        {
            DeferWriterUi(() =>
            {
                HideOverlay();
                _speechService.Stop();
                lock (_writerStateLock)
                {
                    _currentWord = string.Empty;
                    _currentSuggestions = [];
                    _usedSuggestionsInCurrentSession.Clear();
                }

                _hasValidTextInput = false;
                _previousWord = string.Empty;
                _recentWords.Clear();
                _writerAwake = false;
                _loggingService.Information("Writer killed via Ctrl+Q hotkey — all state cleared");
                NotifyWriterSleepTray();
            });
        });

        _hotkeyService.RegisterAction("CleanupWordBank", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("CleanupWordBank ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            DeferWriterUi(() =>
            {
                _predictionService.CleanupNonsensicalEntries();
                _loggingService.Information("Word bank cleanup completed — removed nonsensical entries");
            });
        });

        _hotkeyService.RegisterAction("ResetAllWordBank", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("ResetAllWordBank ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            DeferWriterUi(() =>
            {
                _predictionService.ClearAll();
                _loggingService.Information("Word bank completely reset — all words and phrases cleared");
                _overlayService.SetOverlayStatusHint("Word bank reset complete");
            });
        });

        _hotkeyService.RegisterAction("AddToWordBank", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("AddToWordBank ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            AddCurrentTypingToWordBank();
        });

        _hotkeyService.RegisterAction("AddPhraseToWordBank", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("AddPhraseToWordBank ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            AddCurrentPhraseToWordBank();
        });

        _hotkeyService.RegisterAction("FixClipboardCapitalization", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("FixClipboardCapitalization ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            FixClipboardSentenceCapitalization();
        });

        _hotkeyService.RegisterAction("FixGrammar", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("FixGrammar ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            _ = FixGrammarAsync();
        });

        _hotkeyService.RegisterAction("CompleteSentence", () =>
        {
            if (!_writerAwake)
            {
                _loggingService.Debug("CompleteSentence ignored — writer is sleeping (press wake key to activate)");
                return;
            }
            _ = CompleteSentenceAsync();
        });

        _hotkeyService.RegisterAction("OpenModeMenu", ShowModeMenu);

        _hotkeyService.RegisterAction("OpenLiveCaptions", OpenLiveCaptions);

        _hotkeyService.RegisterAction("OpenSettings", () =>
        {
            // Settings UI not available in Writer-only mode
            _loggingService.Information("OpenSettings hotkey - no settings UI in Writer-only mode");
        });

        _hotkeyService.RegisterAction("OpenStillSpace", () =>
        {
            StillSpaceHotkey.OpenOrFocus(_loggingService);
        });
    }

    private void ShowModeMenu()
    {
        // Mode menu not available in Writer-only mode (no tray icon)
        _loggingService.Information("ModeMenu hotkey - not available in Writer-only mode");
    }

    private void RegisterDefaultHotkeys()
    {
        var settings = _settingsService.Settings.Hotkeys.Bindings;

        if (settings.Count == 0)
        {
            _hotkeyService.RegisterHotkey("VolumeUp", "Ctrl+Shift+Up");
            _hotkeyService.RegisterHotkey("VolumeDown", "Ctrl+Shift+Down");
            _hotkeyService.RegisterHotkey("VolumeMute", "Ctrl+Shift+M");
            _hotkeyService.RegisterHotkey("WriterRefresh", "Ctrl+Shift+R");
            _hotkeyService.RegisterHotkey("ToggleOverlay", "Ctrl+Shift+O");
            _hotkeyService.RegisterHotkey("PauseWriter", "Ctrl+Shift+P");
            _hotkeyService.RegisterHotkey("WakeWriter", "F9", false);
            _hotkeyService.RegisterHotkey("KillWriter", "Ctrl+Q");
            _hotkeyService.RegisterHotkey("CleanupWordBank", "Ctrl+Shift+X");
            _hotkeyService.RegisterHotkey("ResetAllWordBank", "Ctrl+Shift+Delete");
            _hotkeyService.RegisterHotkey("AddToWordBank", "Ctrl+`");
            _hotkeyService.RegisterHotkey("AddPhraseToWordBank", "Ctrl+Shift+`");
            _hotkeyService.RegisterHotkey("FixClipboardCapitalization", "Ctrl+Shift+C");
            _hotkeyService.RegisterHotkey("FixGrammar", "Ctrl+Shift+G");
            _hotkeyService.RegisterHotkey("CompleteSentence", "Ctrl+Shift+S");
            _hotkeyService.RegisterHotkey("OpenModeMenu", _settingsService.Settings.ModeSystem.MenuHotkeyGesture, true);
            _hotkeyService.RegisterHotkey("OpenLiveCaptions", "Ctrl+Shift+L");
            _hotkeyService.RegisterHotkey("OpenSettings", "Ctrl+F3");
            _hotkeyService.RegisterHotkey("OpenStillSpace", "Ctrl+F4", true);

            _loggingService.Information("Registered default hotkeys");
        }
        else
        {
            foreach (var binding in settings.Where(b => b.Enabled))
            {
                var isWake = string.Equals(binding.ActionName, "WakeWriter", StringComparison.OrdinalIgnoreCase);

                // Bare backtick is reserved exclusively for WakeWriter. Skip any other action
                // (e.g. a stale WriterRefresh=` binding) that would otherwise shadow the wake key.
                if (!isWake && IsBareBacktickGesture(binding.Gesture))
                {
                    _loggingService.Information($"Skipping {binding.ActionName}=` — bare backtick is reserved for WakeWriter");
                    continue;
                }

                var consume =
                    string.Equals(binding.ActionName, "OpenModeMenu", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(binding.ActionName, "OpenStillSpace", StringComparison.OrdinalIgnoreCase);
                    // WakeWriter does NOT consume the key — pass-through ensures every press fires
                _hotkeyService.RegisterHotkey(binding.ActionName, binding.Gesture, consume);
            }

            var menuDef = settings.FirstOrDefault(b =>
                string.Equals(b.ActionName, "OpenModeMenu", StringComparison.OrdinalIgnoreCase));
            if (menuDef == null)
            {
                _hotkeyService.RegisterHotkey(
                    "OpenModeMenu",
                    _settingsService.Settings.ModeSystem.MenuHotkeyGesture,
                    true);
            }

            var stillEnabled = settings.FirstOrDefault(b =>
                b.Enabled
                && string.Equals(b.ActionName, "OpenStillSpace", StringComparison.OrdinalIgnoreCase));
            if (stillEnabled == null)
            {
                _hotkeyService.RegisterHotkey("OpenStillSpace", "Ctrl+F4", true);
            }

            // Ensure WakeWriter always has a binding — the writer now sleeps by default and MUST be woken.
            var wakeDef = settings.FirstOrDefault(b =>
                string.Equals(b.ActionName, "WakeWriter", StringComparison.OrdinalIgnoreCase));
            if (wakeDef == null || string.IsNullOrWhiteSpace(wakeDef.Gesture) || !wakeDef.Enabled)
            {
                _hotkeyService.RegisterHotkey("WakeWriter", "F9", false);
                _loggingService.Information("WakeWriter bound to default F9 (no user binding found)");
            }
        }
    }

    private static readonly string[] AllHotkeyActionNames =
    [
        "VolumeUp", "VolumeDown", "VolumeMute", "WriterRefresh",
        "ToggleOverlay", "PauseWriter", "WakeWriter", "KillWriter", "CleanupWordBank", "ResetAllWordBank", "AddToWordBank", "AddPhraseToWordBank",
        "FixClipboardCapitalization", "FixGrammar", "CompleteSentence", "OpenModeMenu", "OpenLiveCaptions", "OpenSettings", "OpenStillSpace",
    ];

    private void ReloadHotkeys()
    {
        foreach (var name in AllHotkeyActionNames)
        {
            _hotkeyService.UnregisterHotkey(name);
        }

        RegisterDefaultHotkeys();
        ApplySpeechSettings();
        _loggingService.Information("Hotkeys and speech settings reloaded from settings");
    }

    private string _previousWord = string.Empty;
    private string _wordBeforePrevious = string.Empty; // For trigram learning

    /// <summary>Runs on the keyboard hook thread — only SendInput + buffer fix; must stay synchronous.</summary>
    private string ApplyWordCapitalizationInjectionIfNeeded(WordTypedEventArgs e)
    {
        var opts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
        var word = e.Word;
        if (!opts.Enabled)
        {
            return word;
        }

        var fixedWord = CapitalizationService.FixCompletedTypedWord(e.TextBeforeWord, e.Word, opts);
        if (string.Equals(fixedWord, e.Word, StringComparison.Ordinal))
        {
            return word;
        }

        _inputService.BeginInjection();
        try
        {
            Win32TextInjection.SendBackspace(e.Word.Length);
            Win32TextInjection.SendText(fixedWord);
            _inputService.ReplaceLastCompletedWord(e.Word, fixedWord);
            return fixedWord;
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Sentence capitalization fix failed: {ex.Message}");
            return word;
        }
        finally
        {
            _inputService.EndInjection();
        }
    }

    /// <summary>Runs on the WPF UI thread via <see cref="DeferWriterUi"/>.</summary>
    private void OnWordTypedDeferred(string word, WordTypedEventArgs e)
    {
        LearnWordOrPhrase(word, bypassVocabularyGate: false);

        if (!string.IsNullOrWhiteSpace(_previousWord) && !string.IsNullOrWhiteSpace(word))
        {
            // Dreamlike: Learn with trigram context for better next-word prediction
            _predictionService.LearnBigramWithContext(_wordBeforePrevious, _previousWord, word);
        }

        _learningEngine.OnWordCommitted(word, e.TextBeforeWord, _writerContext.GetSnapshot());

        _wordBeforePrevious = _previousWord;
        _previousWord = word.Trim().ToLowerInvariant();

        // Always speak completed word out loud
        _speechService.SpeakQueued(word, true);

        AppendContext(word);
    }

    private static string ResolveSuggestionInsertText(SuggestionItem suggestion, string acceptedText)
    {
        if (suggestion.Kind != SuggestionKind.PhraseCompletion || string.IsNullOrWhiteSpace(suggestion.InsertText))
        {
            return acceptedText.TrimEnd();
        }

        var remainder = suggestion.InsertText.TrimStart();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return acceptedText.TrimEnd();
        }

        var startIndex = suggestion.DisplayText.IndexOf(remainder, StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0 && startIndex < acceptedText.Length)
        {
            return acceptedText[startIndex..].TrimStart().TrimEnd();
        }

        return remainder.TrimEnd();
    }

    /// <summary>
    /// Collapses horizontal whitespace runs, trims trailing space, then avoids a doubled gap when the
    /// caret prefix already ends with whitespace and the fragment starts with whitespace (model continuations).
    /// </summary>
    private static string NormalizeSuggestionInsertText(string? rawTextBeforeCaret, string textToInsert)
    {
        if (string.IsNullOrWhiteSpace(textToInsert))
        {
            return string.Empty;
        }

        var t = HorizontalWhitespaceRun.Replace(textToInsert.TrimEnd(), " ");
        if (t.Length == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(rawTextBeforeCaret)
            && char.IsWhiteSpace(rawTextBeforeCaret[^1])
            && char.IsWhiteSpace(t[0]))
        {
            t = t.TrimStart();
        }

        return t;
    }

    private static string SanitizeOverlaySuggestionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var s = text.Trim();

        // Strip leading list markers: "1.", "-", "•", "*", quotes
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^[\d]+[.)\-:]\s*", "");
        s = s.TrimStart('-', '•', '*', '·', '"', '\'', '`').Trim();

        // Collapse internal whitespace
        s = HorizontalWhitespaceRun.Replace(s, " ").Trim();

        // Strip trailing punctuation that doesn't belong (commas, semicolons)
        s = s.TrimEnd(',', ';');

        // Cap phrases at 6 words
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 6)
        {
            s = string.Join(" ", words.Take(6));
        }

        // Sentence-case: capitalise first letter
        if (s.Length > 0)
        {
            s = char.ToUpperInvariant(s[0]) + s[1..];
        }

        return s;
    }

    private static string SanitizeAiSentenceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^[\d]+[.)\-:]\s*", "");
        s = s.TrimStart('-', '•', '*', '·', '"', '\'', '`').Trim();
        s = HorizontalWhitespaceRun.Replace(s, " ").Trim();
        if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s[1..];
        return s;
    }

    private void OnPasteIntercept(object? sender, PasteInterceptEventArgs e)
    {
        var w = _settingsService.Settings.Writer;
        if (!w.AutoCapitalizeSentences)
        {
            return;
        }

        string? inject = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var t = System.Windows.Clipboard.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(t))
                {
                    return;
                }

                inject = CapitalizationService.ApplySentenceCapitalization(t, WriterCapitalizationOptions.From(w));
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"Paste clipboard read failed: {ex.Message}");
            }
        });

        if (string.IsNullOrEmpty(inject))
        {
            return;
        }

        e.SuppressNativePaste = true;
        _inputService.BeginInjection();
        try
        {
            Win32TextInjection.SendText(inject);
            _inputService.AppendPastedPlainText(inject);
            var wAfter = _inputService.GetCurrentWord();
            DeferWriterUi(() => UpdateSuggestions(wAfter));
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Paste inject failed: {ex.Message}");
        }
        finally
        {
            _inputService.EndInjection();
        }
    }

    private void FixClipboardSentenceCapitalization()
    {
        var w = _settingsService.Settings.Writer;
        if (!w.AutoCapitalizeSentences)
        {
            _loggingService.Debug("FixClipboardCapitalization: AutoCapitalizeSentences is off");
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var t = System.Windows.Clipboard.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(t))
                {
                    return;
                }

                var n = CapitalizationService.ApplySentenceCapitalization(t, WriterCapitalizationOptions.From(w));
                if (!string.Equals(n, t, StringComparison.Ordinal))
                {
                    System.Windows.Clipboard.SetText(n);
                    _loggingService.Information("Clipboard text updated with sentence capitalization");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"FixClipboardCapitalization failed: {ex.Message}");
            }
        });
    }

    private async Task FixGrammarAsync()
    {
        try
        {
            // Get current text from the active text field
            var context = _inputService.GetSuggestionContextPrefix();
            var word = _inputService.GetCurrentWord();
            var fullText = _inputService.GetFullSentenceForOverlay();

            _loggingService.Information($"FixGrammar requested for: '{fullText}'");

            // TODO: Integrate with AiGrammarService when available
            // For now, just log the request
            _overlayService.SetOverlayStatusHint("Grammar fix not yet implemented — coming soon!");

            await Task.Delay(2000);
            _overlayService.SetOverlayStatusHint(null);
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"FixGrammarAsync failed: {ex.Message}");
        }
    }

    private async Task CompleteSentenceAsync()
    {
        try
        {
            // Get current text context
            var context = _inputService.GetSuggestionContextPrefix();
            var word = _inputService.GetCurrentWord();
            var fullText = _inputService.GetFullSentenceForOverlay();

            _loggingService.Information($"CompleteSentence requested for: '{fullText}'");

            // TODO: Integrate with AiSentenceCompletionService when available
            // For now, just log the request
            _overlayService.SetOverlayStatusHint("Sentence completion not yet implemented — coming soon!");

            await Task.Delay(2000);
            _overlayService.SetOverlayStatusHint(null);
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"CompleteSentenceAsync failed: {ex.Message}");
        }
    }

    private void OnSentenceTyped(string sentence)
    {
        var normalized = sentence.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        normalized = CapitalizationService.ApplySentenceCapitalization(
            normalized,
            WriterCapitalizationOptions.From(_settingsService.Settings.Writer));

        _predictionService.LearnPhrase(normalized);

        // Delete unused suggestions from this session
        DeleteUnusedSuggestions();

        if (_predictionService is CompositePredictionService compositeSentence)
        {
            compositeSentence.NotifySentenceCommitted(normalized);
        }

        _learningEngine.OnSentenceCompleted(normalized, _writerContext.GetSnapshot());

        if (!_inputService.IsInProtectedField)
        {
            _typingModel.RecordPhrase(normalized, _writerContext.GetSnapshot());
        }

        QueueMongoVocabularyUpsert(normalized, isPhrase: true);

        // Sentence readback when mode is SentencesOnly or Both
        if (_speakMode == SpeakMode.SentencesOnly || _speakMode == SpeakMode.Both)
        {
            _speechService.SpeakQueued(normalized, true);
        }

        _recentWords.Clear();
        _previousWord = string.Empty;
    }

    private void DeleteUnusedSuggestions()
    {
        if (_usedSuggestionsInCurrentSession.Count == 0)
        {
            return;
        }

        // Get all suggestions that were shown but not used in this session
        var unusedSuggestions = _currentSuggestions
            .Where(s => !_usedSuggestionsInCurrentSession.Contains(s.InsertText ?? s.DisplayText))
            .ToList();

        foreach (var unused in unusedSuggestions)
        {
            var textToDelete = unused.InsertText ?? unused.DisplayText;
            
            // Remove from prediction service if it's a word or phrase we learned
            if (unused.Kind == SuggestionKind.WordCompletion || unused.Kind == SuggestionKind.PhraseCompletion)
            {
                _predictionService.RemoveSuggestion(textToDelete);
                _loggingService.Debug($"Deleted unused suggestion: {textToDelete}");
            }
        }

        // Clear the used suggestions for the next session
        _usedSuggestionsInCurrentSession.Clear();
    }

    private void LearnAcceptedSuggestion(
        string acceptedText,
        string? typedPartial = null,
        SuggestionKind? suggestionKind = null,
        int charsDeleted = 0)
    {
        var normalized = acceptedText.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        // Stronger learning for explicitly selected suggestions
        if (normalized.Contains(' '))
        {
            _predictionService.AcceptPhrase(normalized);
        }
        else
        {
            _predictionService.AcceptWord(normalized);
        }

        _learningEngine.OnSuggestionAccepted(normalized, _writerContext.GetSnapshot());

        if (!_inputService.IsInProtectedField
            && typedPartial != null
            && suggestionKind == SuggestionKind.WordCompletion
            && charsDeleted > 0)
        {
            var firstParts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (firstParts.Length > 0)
            {
                var first = firstParts[0];
                var tp = NormalizeTypingWord(typedPartial);
                if (tp.Length > 0 && !string.Equals(tp, first, StringComparison.OrdinalIgnoreCase))
                {
                    _typingModel.RecordCorrection(tp, first);
                }
            }
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            // Learn bigrams/trigrams from accepted suggestion words too
            if (!string.IsNullOrWhiteSpace(_previousWord) && !string.IsNullOrWhiteSpace(part))
            {
                _predictionService.LearnBigramWithContext(_wordBeforePrevious, _previousWord, part);
            }
            _wordBeforePrevious = _previousWord;
            _previousWord = part.Trim().ToLowerInvariant();
            AppendContext(part);
        }
    }

    private static string NormalizeTypingWord(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var chars = input
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            .ToArray();

        return new string(chars).ToLowerInvariant();
    }

    private void LearnWordOrPhrase(string text, bool bypassVocabularyGate)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var isPhrase = normalized.Contains(' ', StringComparison.Ordinal);
        var known = isPhrase
            ? _predictionService.WordBankContainsPhrase(normalized)
            : _predictionService.WordBankContainsWord(normalized);

        if (known)
        {
            ApplyWordBankLearnInternal(normalized, isPhrase);
            return;
        }

        if (bypassVocabularyGate || !IsAiVocabularyGateActive())
        {
            ApplyWordBankLearnInternal(normalized, isPhrase);
            return;
        }

        var context = _inputService.GetSuggestionContextPrefix();
        var gateKey = (isPhrase ? "P:" : "W:") + normalized;
        // Shared task per key: concurrent completions before AI returns only trigger one HTTP call;
        // frequency may under-count in that edge case until the next typed occurrence.
        _ = _vocabGateTasks.GetOrAdd(
            gateKey,
            _ => RunVocabularyGateAndReleaseSlotAsync(normalized, isPhrase, context, gateKey));
    }

    private bool IsAiVocabularyGateActive() =>
        _settingsService.Settings.Ai.EnableVocabularyGate &&
        !string.IsNullOrWhiteSpace(_settingsService.Settings.Ai.ApiKey);

    private void ApplyWordBankLearnInternal(string normalized, bool isPhrase)
    {
        if (isPhrase)
        {
            _predictionService.LearnPhrase(normalized);
        }
        else
        {
            _predictionService.LearnWord(normalized);
        }

        if (!_inputService.IsInProtectedField)
        {
            var snap = _writerContext.GetSnapshot();
            if (isPhrase)
            {
                _typingModel.RecordPhrase(normalized, snap);
            }
            else
            {
                _typingModel.RecordWord(normalized, snap);
            }
        }

        QueueMongoVocabularyUpsert(normalized, isPhrase);
    }

    /// <summary>When the writer learns a word/phrase locally, mirror to Mongo with the current sentence for context.</summary>
    private void QueueMongoVocabularyUpsert(string normalized, bool isPhrase)
    {
        if (!_remoteVocabulary.IsEnabled || _inputService.IsInProtectedField)
        {
            return;
        }

        var sentence = _inputService.GetFullSentenceForOverlay();
        var mode = _writerContext.GetSnapshot().Mode;
        var vocab = _remoteVocabulary;
        var log = _loggingService;
        _ = Task.Run(async () =>
        {
            try
            {
                await vocab
                    .UpsertAsync(normalized, isPhrase, sentence, mode, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Warning($"Mongo vocabulary upsert: {ex.Message}");
            }
        });
    }

    private async Task<bool> RunVocabularyGateAndReleaseSlotAsync(
        string normalized,
        bool isPhrase,
        string? context,
        string gateKey)
    {
        try
        {
            var ok = await _aiVocabGate
                .ShouldRememberNewItemAsync(normalized, isPhrase, context, CancellationToken.None)
                .ConfigureAwait(false);
            if (ok)
            {
                // PredictionService / TypingModel are thread-safe; no WPF touch here.
                ApplyWordBankLearnInternal(normalized, isPhrase);
            }

            return ok;
        }
        finally
        {
            _vocabGateTasks.TryRemove(gateKey, out _);
        }
    }

    private void ApplySpeechSettings()
    {
        var settings = _settingsService.Settings.Speech;
        _speakMode = settings.SpeakMode;

        // Map SpeechRate (int, e.g. -2..+2) to double (0.6..1.8)
        var rate = 1.0 + (settings.SpeechRate * 0.2);
        _speechService.SetRate(rate);

        // Map SpeechVolume (0-100) to float (0.0-1.0)
        _speechService.SetVolume(settings.SpeechVolume / 100f);

        _loggingService.Information(
            $"Speech: voice={_speechService.VoiceName}, rate={rate:F1}, vol={settings.SpeechVolume}, speakMode={_speakMode}, voiceMode={settings.VoiceMode}, route={_speechService.VoiceRouteStatus}");
    }

    private void AppendContext(string word)
    {
        var normalized = word.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _recentWords.Enqueue(normalized);
        while (_recentWords.Count > MaxPhraseContextWords)
        {
            _recentWords.Dequeue();
        }
    }

    private void AddCurrentTypingToWordBank()
    {
        if (!TryMenuAddCurrentWord())
        {
            _loggingService.Information("AddToWordBank requested but there is no current word");
        }
    }

    private void AddCurrentPhraseToWordBank()
    {
        if (!TryMenuAddCurrentPhrase())
        {
            _loggingService.Information("AddPhraseToWordBank requested but there is no current phrase context");
        }
    }

    private bool TryMenuAddCurrentWord()
    {
        if (_inputService.IsInProtectedField)
        {
            _loggingService.Debug("AddToWordBank skipped: protected field");
            return false;
        }

        string cw;
        lock (_writerStateLock)
        {
            cw = _currentWord;
        }

        var candidate = !string.IsNullOrWhiteSpace(cw)
            ? cw
            : _inputService.GetCurrentWord();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        LearnWordOrPhrase(candidate, bypassVocabularyGate: true);
        _loggingService.Information($"Added to word bank: {candidate}");
        return true;
    }

    private bool TryMenuAddCurrentPhrase()
    {
        if (_inputService.IsInProtectedField)
        {
            _loggingService.Debug("AddPhraseToWordBank skipped: protected field");
            return false;
        }

        var phrase = _inputService.GetFullSentenceForOverlay().Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        LearnWordOrPhrase(phrase, bypassVocabularyGate: true);
        _loggingService.Information($"Added phrase to word bank: {phrase}");
        return true;
    }

    private void ApplyApplicationMode(AppMode _)
    {
        // Writer assistance and global hotkeys (volume, word bank, etc.) stay on together.
        _inputService.IsEnabled = true;
    }

    private void OpenChat()
    {
        _loggingService.Information("Opening AI Chat window");
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_chatWindow != null && _chatWindow.IsVisible)
            {
                _chatWindow.Activate();
                return;
            }

            var vm = new ChatViewModel(_chatService, _conversationStore, _chatOptions, _loggingService);
            vm.SetOpenSettingsAction(() => OpenChatSettings(vm));
            _chatWindow = new ChatWindow(vm);
            _chatWindow.Closed += (_, _) => _chatWindow = null;
            _chatWindow.Show();
        });
    }

    private void OpenLiveCaptions()
    {
        _loggingService.Information("Opening Live Captions window");
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_liveCaptionsWindow != null && _liveCaptionsWindow.IsVisible)
            {
                _liveCaptionsWindow.Activate();
                return;
            }

            // Fresh composite service per-open so engine selection (Whisper/Azure/WinRT) re-evaluates.
            var liveSpeech = new CompositeLiveSpeechService(
                () => _settingsService.Settings.Speech,
                () => _settingsService.Settings.LiveCaptions,
                _loggingService);

            LiveCaptionsWindow? window = null;
            var vm = new LiveCaptionsViewModel(
                liveSpeech,
                _loggingService,
                _settingsService,
                onFullscreenRequested: on => window?.SetFullscreen(on));

            window = new LiveCaptionsWindow(vm);
            _liveCaptionsWindow = window;
            _liveCaptionsWindow.Closed += (_, _) =>
            {
                liveSpeech.Dispose();
                _liveCaptionsWindow = null;
            };
            _liveCaptionsWindow.Show();
        });
    }

    private void OpenChatSettings(ChatViewModel chatVm)
    {
        var settingsVm = new ChatSettingsViewModel(_chatService, _chatOptions, _loggingService, () =>
        {
            // Persist all chat fields to app settings after Save
            var ai = _settingsService.Settings.Ai;
            ai.ApiBaseUrl = _chatOptions.BaseUrl;
            ai.ApiKey = _chatOptions.ApiKey;
            ai.Model = _chatOptions.Model;
            ai.ChatUseStreaming = _chatOptions.UseStreaming;
            ai.ChatTemperature = _chatOptions.Temperature;
            ai.ChatTimeoutSeconds = _chatOptions.TimeoutSeconds;
            ai.ChatSystemPrompt = _chatOptions.DefaultSystemPrompt;
            _settingsService.Save();
            _loggingService.Information("[ChatSettings] Persisted to app settings");
        });
        var settingsWindow = new ChatSettingsWindow(settingsVm);
        if (_chatWindow != null)
            settingsWindow.Owner = _chatWindow;
        settingsWindow.ShowDialog();
    }

    private ChatOptions LoadChatOptions()
    {
        var ai = _settingsService.Settings.Ai;
        return new ChatOptions
        {
            BaseUrl = ai.ApiBaseUrl ?? "https://api.openai.com/v1",
            ApiKey = ai.ApiKey ?? "",
            Model = ai.Model ?? "gpt-4o-mini",
            UseStreaming = ai.ChatUseStreaming,
            Temperature = ai.ChatTemperature,
            TimeoutSeconds = ai.ChatTimeoutSeconds,
            DefaultSystemPrompt = ai.ChatSystemPrompt,
        };
    }

    private static bool IsBareBacktickGesture(string gesture)
    {
        var trimmed = gesture.Trim();
        return trimmed.Equals("`", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("GRAVE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWriterCaretAvailable()
    {
        if (Win32Caret.GetCaretPosition(out var x, out var y) && (x != 0 || y != 0))
        {
            return true;
        }

        if (Win32Caret.TryGetTextInputBounds(out var bounds) && !bounds.IsEmpty)
        {
            return true;
        }

        return Win32Caret.TryGetCaretScreenRect(out var caretRc) && !caretRc.IsEmpty;
    }

    private string GetWakeWriterHotkeyDisplay()
    {
        var binding = _settingsService.Settings.Hotkeys.Bindings
            .FirstOrDefault(b => string.Equals(b.ActionName, "WakeWriter", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(binding?.Gesture) ? "F9" : binding!.Gesture;
    }

    private void NotifyWriterWakeTray()
    {
        // Tray notification removed - Writer is main window
        _loggingService.Information("Writer awake - ready for typing");
    }

    private void NotifyWriterSleepTray()
    {
        // Tray notification removed - Writer is main window
        _loggingService.Information("Writer sleeping - press wake key to show window again");
    }

    public void Dispose()
    {
        if (_voiceBridgeListener != null)
        {
            _voiceBridgeListener.MessageReceived -= OnVoiceBridgeMessageReceived;
            _voiceBridgeListener.ConnectionChanged -= OnVoiceBridgeConnectionChanged;
            _voiceBridgeListener.Dispose();
            _voiceBridgeListener = null;
        }

        _overlayAiDebounceCts?.Cancel();
        _overlayAiDebounceCts?.Dispose();
        _overlayAiDebounceCts = null;

        _highlightSpeechCts?.Cancel();
        _highlightSpeechCts?.Dispose();
        _highlightSpeechCts = null;
        _focusCheckTimer.Stop();
        _focusCheckTimer.Dispose();
        _inputService.Dispose();
        _overlayService.Dispose();
        _hotkeyService.Dispose();
        // _trayIconService.Dispose();
        _typingModel.Save();
        if (_typingModel is IDisposable typingDisposable)
        {
            typingDisposable.Dispose();
        }

        if (_predictionService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        if (_speechService is IDisposable speechDisposable)
        {
            speechDisposable.Dispose();
        }
        if (_chatService is IDisposable chatDisposable)
        {
            chatDisposable.Dispose();
        }

        if (_aiSuggestionService is IDisposable aiSuggestDisposable)
        {
            aiSuggestDisposable.Dispose();
        }

        _aiVocabGate.Dispose();
        _localOverlayHttp.Dispose();
        _loggingService.Information("Application shutdown");
    }
}
