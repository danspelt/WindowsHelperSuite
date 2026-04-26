package com.voicecapture.model

data class CaptionUiState(
    val isListening: Boolean = false,
    val statusText: String = "Idle",
    val partialText: String = "",
    val finalTranscript: String = "",
    val displayText: String = "Press Start Listening",
    val appendMode: Boolean = true,
    val singleSentenceMode: Boolean = false, // true = show only current utterance, replacing in real-time
    val fontScaleSp: Float = 56f,
    val keepScreenAwake: Boolean = true,
    val micPermissionGranted: Boolean = false,
    val useOpenAiWhisper: Boolean = false,
    val openAiApiKey: String = "",
    val openAiWhisperPrompt: String = "The speaker has cerebral palsy with dysarthric speech. Transcribe faithfully and prefer likely intended words.",
    val bridgeHost: String = "",
    val bridgePort: Int = 53742,
    val bridgeToken: String = "",
    val bridgeAutoConnect: Boolean = false,
    val bridgeConnected: Boolean = false,
    val bridgeStatus: String = "Disconnected",
)
