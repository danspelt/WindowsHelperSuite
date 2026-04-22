package com.voicecapture.model

data class VoiceCaptureSettings(
    val appendMode: Boolean = true,
    val fontScaleSp: Float = 56f,
    val keepScreenAwake: Boolean = true,
    val languageTag: String = "en-US",
)
