package com.voicecapture.speech

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Wraps [SpeechRecognizer] with partial results and automatic restart between phrases
 * while [sessionActive] is true (user-started continuous capture until [stop]).
 */
class AndroidSpeechRecognizerManager(
    private val context: Context,
    private val onPartial: (String) -> Unit,
    private val onFinal: (String) -> Unit,
    private val onError: (String) -> Unit,
    private val onListeningStateChanged: (Boolean) -> Unit,
) {
    private val mainHandler = Handler(Looper.getMainLooper())
    private var recognizer: SpeechRecognizer? = null
    private val sessionActive = AtomicBoolean(false)
    private var languageTag: String = "en-US"

    fun start(languageTag: String = "en-US") {
        stop()
        this.languageTag = languageTag
        sessionActive.set(true)

        if (!SpeechRecognizer.isRecognitionAvailable(context.applicationContext)) {
            sessionActive.set(false)
            onError("Speech service unavailable on this device.")
            return
        }

        val appContext = context.applicationContext
        recognizer = SpeechRecognizer.createSpeechRecognizer(appContext).apply {
            setRecognitionListener(buildListener())
            startListening(buildIntent())
        }
    }

    fun stop() {
        sessionActive.set(false)
        recognizer?.run {
            try {
                stopListening()
            } catch (_: Exception) {
            }
            try {
                cancel()
            } catch (_: Exception) {
            }
            try {
                destroy()
            } catch (_: Exception) {
            }
        }
        recognizer = null
        onListeningStateChanged(false)
    }

    private fun buildIntent(): Intent =
        Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(
                RecognizerIntent.EXTRA_LANGUAGE_MODEL,
                RecognizerIntent.LANGUAGE_MODEL_FREE_FORM,
            )
            putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
            putExtra(RecognizerIntent.EXTRA_LANGUAGE, languageTag)
            putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 5)
        }

    private fun scheduleRestart(delayMs: Long = 120L) {
        if (!sessionActive.get()) return
        mainHandler.postDelayed({
            if (!sessionActive.get()) return@postDelayed
            val rec = recognizer ?: return@postDelayed
            try {
                rec.startListening(buildIntent())
            } catch (e: Exception) {
                onError("Could not restart listening: ${e.message}")
                stop()
            }
        }, delayMs)
    }

    private fun buildListener(): RecognitionListener =
        object : RecognitionListener {
            override fun onReadyForSpeech(params: Bundle?) {
                onListeningStateChanged(true)
            }

            override fun onBeginningOfSpeech() {}

            override fun onRmsChanged(rmsdB: Float) {}

            override fun onBufferReceived(buffer: ByteArray?) {}

            override fun onEndOfSpeech() {}

            override fun onError(error: Int) {
                val message = speechErrorToMessage(error)
                when (error) {
                    SpeechRecognizer.ERROR_NO_MATCH,
                    SpeechRecognizer.ERROR_SPEECH_TIMEOUT,
                    -> {
                        if (sessionActive.get()) {
                            scheduleRestart(180L)
                        } else {
                            onListeningStateChanged(false)
                        }
                    }
                    SpeechRecognizer.ERROR_NETWORK,
                    SpeechRecognizer.ERROR_NETWORK_TIMEOUT,
                    -> {
                        if (sessionActive.get()) {
                            onError(message)
                            scheduleRestart(400L)
                        } else {
                            onListeningStateChanged(false)
                            onError(message)
                        }
                    }
                    SpeechRecognizer.ERROR_CLIENT,
                    SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS,
                    -> {
                        onListeningStateChanged(false)
                        onError(message)
                        stop()
                    }
                    else -> {
                        onError(message)
                        if (sessionActive.get()) {
                            scheduleRestart(350L)
                        } else {
                            onListeningStateChanged(false)
                        }
                    }
                }
            }

            override fun onResults(results: Bundle) {
                val text =
                    results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                        ?.firstOrNull()
                        ?.trim()
                        .orEmpty()
                if (text.isNotBlank()) {
                    onFinal(text)
                }
                if (sessionActive.get()) {
                    scheduleRestart()
                } else {
                    onListeningStateChanged(false)
                }
            }

            override fun onPartialResults(partialResults: Bundle) {
                val text =
                    partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                        ?.firstOrNull()
                        ?.trim()
                        .orEmpty()
                if (text.isNotBlank()) {
                    onPartial(text)
                }
            }

            override fun onEvent(eventType: Int, params: Bundle?) {}
        }

    private fun speechErrorToMessage(error: Int): String =
        when (error) {
            SpeechRecognizer.ERROR_AUDIO -> "Audio recording error."
            SpeechRecognizer.ERROR_CLIENT -> "Recognition client error — try Stop then Start."
            SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> "Microphone permission needed."
            SpeechRecognizer.ERROR_NETWORK -> "Network error for speech recognition."
            SpeechRecognizer.ERROR_NETWORK_TIMEOUT -> "Network timeout."
            SpeechRecognizer.ERROR_NO_MATCH -> "No speech detected."
            SpeechRecognizer.ERROR_RECOGNIZER_BUSY -> "Recognizer busy."
            SpeechRecognizer.ERROR_SERVER -> "Recognition server error."
            SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> "Speech input timed out."
            else -> "Speech error ($error)."
        }
}
