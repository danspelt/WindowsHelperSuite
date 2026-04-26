package com.voicecapture.viewmodel

import android.app.Application
import android.os.Build
import android.provider.Settings
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.Manifest
import android.content.pm.PackageManager
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.voicecapture.bridge.BridgeClient
import com.voicecapture.data.SettingsRepository
import com.voicecapture.model.CaptionUiState
import com.voicecapture.speech.AndroidSpeechRecognizerManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.Job
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext

class VoiceCaptureViewModel(
    application: Application,
    private val settingsRepository: SettingsRepository,
) : AndroidViewModel(application) {

    private val _uiState = MutableStateFlow(CaptionUiState())
    val uiState: StateFlow<CaptionUiState> = _uiState.asStateFlow()

    private var settingsLanguage: String = "en-US"
    private var autoStartedThisProcess: Boolean = false

    private val bridgeClient =
        BridgeClient(
            deviceIdProvider = {
                Settings.Secure.getString(
                    application.applicationContext.contentResolver,
                    Settings.Secure.ANDROID_ID
                ) ?: "android"
            },
            appVersionProvider = { Build.VERSION.RELEASE ?: "unknown" },
            onCommand = { cmd, _ ->
                when (cmd) {
                    "startListening" -> startListening()
                    "stopListening" -> stopListening()
                }
            },
            onTextReceived = { text ->
                _uiState.update { it.copy(statusText = "PC: $text") }
            },
            onState = { connected, status ->
                _uiState.update { it.copy(bridgeConnected = connected, bridgeStatus = status) }
            },
        )

    private var audioStreamJob: Job? = null

    private val speechManager =
        AndroidSpeechRecognizerManager(
            context = application.applicationContext,
            onPartial = { text ->
                _uiState.update { state ->
                    // In single sentence mode, clear final transcript so we only show current utterance
                    val newFinal = if (state.singleSentenceMode) "" else state.finalTranscript
                    state.copy(
                        partialText = text,
                        finalTranscript = newFinal
                    ).let { s ->
                        s.copy(displayText = recomputeDisplay(s))
                    }
                }
            },
            onFinal = { text ->
                _uiState.update { state ->
                    val next = when {
                        state.singleSentenceMode -> {
                            // Single sentence mode: show this final result, discard previous
                            state.copy(
                                partialText = "",
                                finalTranscript = text,
                                statusText = "Recognized",
                            )
                        }
                        state.appendMode -> {
                            val prior = state.finalTranscript
                            val merged = if (prior.isBlank()) text else "$prior $text"
                            state.copy(
                                partialText = "",
                                finalTranscript = merged,
                                statusText = "Recognized",
                            )
                        }
                        else -> {
                            state.copy(
                                partialText = "",
                                finalTranscript = text,
                                statusText = "Recognized",
                            )
                        }
                    }
                    next.copy(displayText = recomputeDisplay(next))
                }
            },
            onError = { message ->
                _uiState.update {
                    it.copy(
                        statusText = message,
                    )
                }
            },
            onListeningStateChanged = { listening ->
                _uiState.update {
                    it.copy(
                        isListening = listening,
                        statusText = if (listening) "Listening" else "Stopped",
                    )
                }
            },
        )

    init {
        viewModelScope.launch {
            settingsRepository.settings.collect { s ->
                settingsLanguage = s.languageTag
                _uiState.update { curr ->
                    val next =
                        curr.copy(
                            appendMode = s.appendMode,
                            singleSentenceMode = s.singleSentenceMode,
                            fontScaleSp = s.fontScaleSp.coerceIn(32f, 96f),
                            keepScreenAwake = s.keepScreenAwake,
                            useOpenAiWhisper = s.useOpenAiWhisper,
                            openAiApiKey = s.openAiApiKey,
                            openAiWhisperPrompt = s.openAiWhisperPrompt,
                            bridgeHost = s.bridgeHost,
                            bridgePort = s.bridgePort,
                            bridgeToken = s.bridgeToken,
                            bridgeAutoConnect = s.bridgeAutoConnect,
                        )
                    next.copy(displayText = recomputeDisplay(next))
                }

                if (s.bridgeAutoConnect &&
                    s.bridgeHost.isNotBlank() &&
                    s.bridgeToken.isNotBlank() &&
                    !_uiState.value.bridgeConnected
                ) {
                    connectBridge()
                }
            }
        }
    }

    fun setMicPermissionGranted(granted: Boolean) {
        _uiState.update { it.copy(micPermissionGranted = granted) }
    }

    fun startListeningIfAllowedAndNotAlreadyStarted() {
        if (autoStartedThisProcess) return
        if (!_uiState.value.micPermissionGranted) return
        autoStartedThisProcess = true
        startListening()
    }

    fun startListening() {
        val ctx = getApplication<Application>()
        if (
            ContextCompat.checkSelfPermission(ctx, Manifest.permission.RECORD_AUDIO) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            _uiState.update {
                it.copy(statusText = "Microphone permission needed")
            }
            return
        }

        // Keep UI stable: show "listening" immediately, even while the recognizer warms up.
        _uiState.update { curr ->
            val next =
                curr.copy(
                    isListening = true,
                    statusText = "Listening…",
                )
            next.copy(displayText = recomputeDisplay(next))
        }
        speechManager.start(settingsLanguage)
        startBridgeAudioStreamIfPossible()
    }

    fun stopListening() {
        stopBridgeAudioStream()
        speechManager.stop()
        _uiState.update {
            val next =
                it.copy(
                    isListening = false,
                    statusText = "Stopped",
                )
            next.copy(displayText = recomputeDisplay(next))
        }
    }

    fun connectBridge() {
        val s = _uiState.value
        if (s.bridgeHost.isBlank() || s.bridgeToken.isBlank()) {
            _uiState.update { it.copy(bridgeStatus = "Missing host/token") }
            return
        }
        bridgeClient.connect(s.bridgeHost.trim(), s.bridgePort, s.bridgeToken.trim())
    }

    fun disconnectBridge() {
        bridgeClient.disconnect()
    }

    fun setBridgeHost(value: String) {
        _uiState.update { it.copy(bridgeHost = value) }
        viewModelScope.launch { settingsRepository.setBridgeHost(value) }
    }

    fun setBridgePort(value: Int) {
        _uiState.update { it.copy(bridgePort = value) }
        viewModelScope.launch { settingsRepository.setBridgePort(value) }
    }

    fun setBridgeToken(value: String) {
        _uiState.update { it.copy(bridgeToken = value) }
        viewModelScope.launch { settingsRepository.setBridgeToken(value) }
    }

    fun setBridgeAutoConnect(enabled: Boolean) {
        _uiState.update { it.copy(bridgeAutoConnect = enabled) }
        viewModelScope.launch { settingsRepository.setBridgeAutoConnect(enabled) }
    }

    fun clearText() {
        _uiState.update {
            it.copy(
                partialText = "",
                finalTranscript = "",
                displayText = if (it.isListening) "Listening…" else "Press Start Listening",
                statusText = "Cleared",
            )
        }
    }

    fun copyTranscript() {
        val text = _uiState.value.displayText
        if (text == "Press Start Listening" || text == "Listening…") {
            _uiState.update { it.copy(statusText = "Nothing to copy") }
            return
        }
        val cm =
            getApplication<Application>()
                .getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText("Voice Capture", text))
        _uiState.update { it.copy(statusText = "Copied to clipboard") }
    }

    fun setAppendMode(enabled: Boolean) {
        viewModelScope.launch {
            settingsRepository.setAppendMode(enabled)
        }
    }

    fun setSingleSentenceMode(enabled: Boolean) {
        viewModelScope.launch {
            settingsRepository.setSingleSentenceMode(enabled)
        }
    }

    fun setFontScaleSp(value: Float) {
        val clamped = value.coerceIn(32f, 96f)
        viewModelScope.launch {
            settingsRepository.setFontScaleSp(clamped)
        }
    }

    fun setKeepScreenAwake(enabled: Boolean) {
        viewModelScope.launch {
            settingsRepository.setKeepScreenAwake(enabled)
        }
    }

    override fun onCleared() {
        super.onCleared()
        speechManager.stop()
        stopBridgeAudioStream()
        bridgeClient.disconnect()
    }

    private fun startBridgeAudioStreamIfPossible() {
        val state = _uiState.value
        if (!state.bridgeConnected) return
        if (!state.micPermissionGranted) return
        if (audioStreamJob != null) return

        audioStreamJob =
            viewModelScope.launch(Dispatchers.IO) {
                val sampleRate = 16000
                val channelConfig = AudioFormat.CHANNEL_IN_MONO
                val audioFormat = AudioFormat.ENCODING_PCM_16BIT
                val minBuf = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
                val bufSize = maxOf(minBuf, sampleRate / 10 * 2) // ~100ms
                val buffer = ByteArray(bufSize)

                val record =
                    AudioRecord(
                        MediaRecorder.AudioSource.MIC,
                        sampleRate,
                        channelConfig,
                        audioFormat,
                        bufSize * 2
                    )

                if (record.state != AudioRecord.STATE_INITIALIZED) {
                    _uiState.update { it.copy(bridgeStatus = "AudioRecord init failed") }
                    record.release()
                    audioStreamJob = null
                    return@launch
                }

                var seq = 0
                try {
                    record.startRecording()
                    while (isActive && _uiState.value.isListening && _uiState.value.bridgeConnected) {
                        val read = record.read(buffer, 0, buffer.size)
                        if (read > 0) {
                            bridgeClient.sendAudioChunk(
                                seq = seq++,
                                sampleRate = sampleRate,
                                channels = 1,
                                pcm16le = buffer.copyOf(read),
                            )
                        }
                    }
                } catch (_: Exception) {
                } finally {
                    try {
                        record.stop()
                    } catch (_: Exception) {
                    }
                    record.release()
                    audioStreamJob = null
                }
            }
    }

    private fun stopBridgeAudioStream() {
        audioStreamJob?.cancel()
        audioStreamJob = null
    }

    private fun recomputeDisplay(state: CaptionUiState): String {
        val partial = state.partialText
        val finalT = state.finalTranscript
        return if (state.appendMode) {
            listOf(finalT, partial)
                .filter { it.isNotBlank() }
                .joinToString(" ")
                .ifBlank {
                    if (state.isListening) "Listening…" else "Press Start Listening"
                }
        } else {
            partial.takeIf { it.isNotBlank() }
                ?: finalT.takeIf { it.isNotBlank() }
                ?: if (state.isListening) "Listening…" else "Press Start Listening"
        }
    }
}
