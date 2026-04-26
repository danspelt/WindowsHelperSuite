package com.voicecapture.model

data class VoiceCaptureSettings(
    val appendMode: Boolean = true,
    val singleSentenceMode: Boolean = false,
    val fontScaleSp: Float = 56f,
    val keepScreenAwake: Boolean = true,
    val languageTag: String = "en-US",
    val useOpenAiWhisper: Boolean = false,
    val openAiApiKey: String = "",
    val openAiWhisperPrompt: String = "The speaker has cerebral palsy with dysarthric speech. Transcribe faithfully and prefer likely intended words.",
    val bridgeHost: String = "",
    val bridgePort: Int = 53742,
    val bridgeToken: String = "",
    val bridgeAutoConnect: Boolean = false,
)
