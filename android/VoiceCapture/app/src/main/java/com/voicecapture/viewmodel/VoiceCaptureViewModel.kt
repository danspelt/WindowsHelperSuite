package com.voicecapture.viewmodel

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.Manifest
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.voicecapture.data.SettingsRepository
import com.voicecapture.model.CaptionUiState
import com.voicecapture.speech.AndroidSpeechRecognizerManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

class VoiceCaptureViewModel(
    application: Application,
    private val settingsRepository: SettingsRepository,
) : AndroidViewModel(application) {

    private val _uiState = MutableStateFlow(CaptionUiState())
    val uiState: StateFlow<CaptionUiState> = _uiState.asStateFlow()

    private var settingsLanguage: String = "en-US"

    private val speechManager =
        AndroidSpeechRecognizerManager(
            context = application.applicationContext,
            onPartial = { text ->
                _uiState.update { state ->
                    state.copy(partialText = text).let { s ->
                        s.copy(displayText = recomputeDisplay(s))
                    }
                }
            },
            onFinal = { text ->
                _uiState.update { state ->
                    val prior = state.finalTranscript
                    val merged =
                        if (state.appendMode) {
                            if (prior.isBlank()) text else "$prior $text"
                        } else {
                            text
                        }
                    val next =
                        state.copy(
                            partialText = "",
                            finalTranscript = merged,
                            statusText = "Recognized",
                        )
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
                            fontScaleSp = s.fontScaleSp.coerceIn(32f, 96f),
                            keepScreenAwake = s.keepScreenAwake,
                        )
                    next.copy(displayText = recomputeDisplay(next))
                }
            }
        }
    }

    fun setMicPermissionGranted(granted: Boolean) {
        _uiState.update { it.copy(micPermissionGranted = granted) }
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

        _uiState.update { it.copy(statusText = "Starting…") }
        speechManager.start(settingsLanguage)
    }

    fun stopListening() {
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
