package com.voicecapture.speech

import android.content.Context
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.*
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * OpenAI Whisper speech recognizer for better accuracy with atypical speech patterns.
 * Records audio in chunks and sends to OpenAI Whisper API for transcription.
 */
class OpenAiWhisperRecognizer(
    private val context: Context,
    private val apiKey: String,
    private val prompt: String,
    private val onPartial: (String) -> Unit,
    private val onFinal: (String) -> Unit,
    private val onError: (String) -> Unit,
    private val onListeningStateChanged: (Boolean) -> Unit,
) {
    private val mainHandler = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(30, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build()

    private var audioRecord: AudioRecord? = null
    private val isRecording = AtomicBoolean(false)
    private var recordingJob: Job? = null
    private var processJob: Job? = null

    // Audio configuration: 16kHz, mono, 16-bit PCM (Whisper optimal)
    private val sampleRate = 16000
    private val channelConfig = AudioFormat.CHANNEL_IN_MONO
    private val audioFormat = AudioFormat.ENCODING_PCM_16BIT
    private val bufferSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)

    // Process audio in 3-second chunks
    private val chunkDurationMs = 3000
    private val bytesPerSecond = sampleRate * 2 // 16-bit = 2 bytes per sample
    private val chunkSize = bytesPerSecond * (chunkDurationMs / 1000)

    fun start() {
        stop()
        isRecording.set(true)

        try {
            audioRecord = AudioRecord(
                MediaRecorder.AudioSource.MIC,
                sampleRate,
                channelConfig,
                audioFormat,
                bufferSize * 2
            )

            if (audioRecord?.state != AudioRecord.STATE_INITIALIZED) {
                onError("Failed to initialize audio recorder")
                return
            }

            audioRecord?.startRecording()
            onListeningStateChanged(true)

            recordingJob = scope.launch {
                recordAndProcess()
            }
        } catch (e: Exception) {
            onError("Audio recording error: ${e.message}")
            stop()
        }
    }

    fun stop() {
        isRecording.set(false)
        recordingJob?.cancel()
        processJob?.cancel()

        try {
            audioRecord?.stop()
            audioRecord?.release()
        } catch (_: Exception) {
        }
        audioRecord = null

        mainHandler.post {
            onListeningStateChanged(false)
        }
    }

    private suspend fun recordAndProcess() {
        val buffer = ByteArray(bufferSize)
        val chunkBuffer = ByteArrayOutputStream()

        try {
            while (isRecording.get() && isActive) {
                val read = audioRecord?.read(buffer, 0, buffer.size) ?: 0
                if (read > 0) {
                    chunkBuffer.write(buffer, 0, read)

                    // Process when we have enough data
                    if (chunkBuffer.size() >= chunkSize) {
                        val audioData = chunkBuffer.toByteArray()
                        chunkBuffer.reset()

                        // Keep some overlap for continuity (0.5 second)
                        val overlapSize = bytesPerSecond / 2
                        if (audioData.size > overlapSize) {
                            chunkBuffer.write(audioData, audioData.size - overlapSize, overlapSize)
                        }

                        // Process this chunk
                        processJob = scope.launch {
                            processAudioChunk(audioData, isFinal = false)
                        }
                    }
                }
            }

            // Process remaining audio as final
            if (chunkBuffer.size() > bytesPerSecond) { // At least 1 second
                val finalAudio = chunkBuffer.toByteArray()
                processAudioChunk(finalAudio, isFinal = true)
            }
        } catch (e: CancellationException) {
            // Normal cancellation
        } catch (e: Exception) {
            mainHandler.post {
                onError("Recording error: ${e.message}")
            }
        }
    }

    private suspend fun processAudioChunk(pcmData: ByteArray, isFinal: Boolean) {
        try {
            // Convert PCM to WAV
            val wavData = pcmToWav(pcmData)

            // Save to temp file
            val tempFile = File.createTempFile("whisper_chunk", ".wav", context.cacheDir)
            FileOutputStream(tempFile).use { it.write(wavData) }

            // Send to OpenAI
            val transcription = transcribeWithWhisper(tempFile)

            // Clean up
            tempFile.delete()

            if (transcription.isNotBlank()) {
                mainHandler.post {
                    if (isFinal) {
                        onFinal(transcription)
                    } else {
                        onPartial(transcription)
                    }
                }
            }
        } catch (e: Exception) {
            // Don't report errors for partial results, only for final
            if (isFinal) {
                mainHandler.post {
                    onError("Transcription failed: ${e.message}")
                }
            }
        }
    }

    private fun pcmToWav(pcmData: ByteArray): ByteArray {
        val byteRate = sampleRate * 2 // 16-bit mono
        val totalDataLen = pcmData.size + 36
        val wavSize = pcmData.size

        val header = ByteBuffer.allocate(44)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                // RIFF header
                put("RIFF".toByteArray())
                putInt(totalDataLen)
                put("WAVE".toByteArray())
                // fmt chunk
                put("fmt ".toByteArray())
                putInt(16) // Subchunk1Size (16 for PCM)
                putShort(1) // AudioFormat (1 for PCM)
                putShort(1) // NumChannels (1 for mono)
                putInt(sampleRate)
                putInt(byteRate)
                putShort(2) // BlockAlign
                putShort(16) // BitsPerSample
                // data chunk
                put("data".toByteArray())
                putInt(wavSize)
            }
            .array()

        return header + pcmData
    }

    private fun transcribeWithWhisper(audioFile: File): String {
        if (apiKey.isBlank()) {
            throw IllegalStateException("OpenAI API key not configured")
        }

        val requestBody = MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .addFormDataPart("model", "whisper-1")
            .addFormDataPart("language", "en")
            .addFormDataPart("response_format", "json")
            .apply {
                if (prompt.isNotBlank()) {
                    addFormDataPart("prompt", prompt)
                }
            }
            .addFormDataPart(
                "file",
                audioFile.name,
                audioFile.readBytes().toRequestBody("audio/wav".toMediaTypeOrNull())
            )
            .build()

        val request = Request.Builder()
            .url("https://api.openai.com/v1/audio/transcriptions")
            .header("Authorization", "Bearer $apiKey")
            .post(requestBody)
            .build()

        httpClient.newCall(request).execute().use { response ->
            if (!response.isSuccessful) {
                throw RuntimeException("API error ${response.code}: ${response.body?.string()}")
            }

            val json = response.body?.string() ?: return ""
            val jsonObject = JSONObject(json)
            return jsonObject.optString("text", "").trim()
        }
    }

    fun isAvailable(): Boolean = apiKey.isNotBlank()
}
