package com.voicecapture.model

data class CaptionUiState(
    val isListening: Boolean = false,
    val statusText: String = "Idle",
    val partialText: String = "",
    val finalTranscript: String = "",
    val displayText: String = "Press Start Listening",
    val appendMode: Boolean = true,
    val fontScaleSp: Float = 56f,
    val keepScreenAwake: Boolean = true,
    val micPermissionGranted: Boolean = false,
)
