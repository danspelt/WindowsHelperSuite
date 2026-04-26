package com.voicecapture.bridge

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import android.util.Base64

class BridgeClient(
    private val deviceIdProvider: () -> String,
    private val appVersionProvider: () -> String,
    private val onCommand: (command: String, args: Map<String, String>) -> Unit,
    private val onTextReceived: (text: String) -> Unit,
    private val onState: (connected: Boolean, status: String) -> Unit,
) {
    private val client =
        OkHttpClient.Builder()
            .pingInterval(15, TimeUnit.SECONDS)
            .connectTimeout(8, TimeUnit.SECONDS)
            .readTimeout(0, TimeUnit.SECONDS)
            .build()

    private var socket: WebSocket? = null
    private var sessionId: String? = null
    private var sharedToken: String = ""

    fun connect(host: String, port: Int, token: String) {
        disconnect()
        sharedToken = token
        sessionId = null

        val url = "ws://$host:$port/ws?token=$token"
        onState(false, "Connecting…")

        val req = Request.Builder().url(url).build()
        socket =
            client.newWebSocket(
                req,
                object : WebSocketListener() {
                    override fun onOpen(webSocket: WebSocket, response: Response) {
                        sendHello()
                        onState(true, "Connected")
                    }

                    override fun onMessage(webSocket: WebSocket, text: String) {
                        handleMessage(text)
                    }

                    override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                        onState(false, "Closed ($code)")
                    }

                    override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                        onState(false, "Failed: ${t.message ?: "unknown"}")
                    }
                },
            )
    }

    fun disconnect() {
        try {
            socket?.close(1000, "bye")
        } catch (_: Exception) {
        }
        socket = null
        sessionId = null
        onState(false, "Disconnected")
    }

    fun sendText(text: String) {
        val ws = socket ?: return
        val msg =
            JSONObject()
                .put("type", "text_send")
                .put("sessionId", sessionId)
                .put("text", text)
        ws.send(msg.toString())
    }

    fun sendAudioChunk(
        seq: Int,
        sampleRate: Int,
        channels: Int,
        pcm16le: ByteArray,
    ) {
        val ws = socket ?: return
        val b64 = Base64.encodeToString(pcm16le, Base64.NO_WRAP)
        val msg =
            JSONObject()
                .put("type", "audio_chunk")
                .put("sessionId", sessionId)
                .put("seq", seq)
                .put("audioFormat", "pcm16le")
                .put("sampleRate", sampleRate)
                .put("channels", channels)
                .put("audioBase64", b64)
        ws.send(msg.toString())
    }

    private fun sendHello() {
        val ws = socket ?: return
        val msg =
            JSONObject()
                .put("type", "hello")
                .put("deviceId", deviceIdProvider())
                .put("appVersion", appVersionProvider())
        ws.send(msg.toString())
    }

    private fun handleMessage(text: String) {
        val json = try {
            JSONObject(text)
        } catch (_: Exception) {
            return
        }

        val type = json.optString("type", "")
        if (type.isBlank()) return

        if (json.has("sessionId")) {
            val sid = json.optString("sessionId", "")
            if (sid.isNotBlank()) sessionId = sid
        }

        when (type) {
            "auth_challenge" -> {
                val nonceHex = json.optString("nonce", "")
                if (nonceHex.isBlank() || sharedToken.isBlank()) return
                val hmacHex = hmacSha256Hex(sharedToken, hexToBytes(nonceHex))
                val resp =
                    JSONObject()
                        .put("type", "auth_response")
                        .put("sessionId", sessionId)
                        .put("hmac", hmacHex)
                socket?.send(resp.toString())
            }

            "text_received" -> {
                val t = json.optString("text", "")
                if (t.isNotBlank()) onTextReceived(t)
            }

            "command" -> {
                val cmd = json.optString("command", "")
                val argsObj = json.optJSONObject("args")
                val args =
                    buildMap {
                        if (argsObj != null) {
                            for (k in argsObj.keys()) {
                                put(k, argsObj.optString(k, ""))
                            }
                        }
                    }
                if (cmd.isNotBlank()) {
                    onCommand(cmd, args)
                }

                val ack =
                    JSONObject()
                        .put("type", "command_ack")
                        .put("sessionId", sessionId)
                        .put("correlationId", json.optString("correlationId", ""))
                        .put("result", "ok")
                socket?.send(ack.toString())
            }
        }
    }

    private fun hmacSha256Hex(key: String, data: ByteArray): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        val out = mac.doFinal(data)
        return out.joinToString("") { b -> "%02x".format(b) }
    }

    private fun hexToBytes(hex: String): ByteArray {
        val clean = hex.trim()
        if (clean.length % 2 != 0) return ByteArray(0)
        val out = ByteArray(clean.length / 2)
        for (i in out.indices) {
            val index = i * 2
            out[i] = clean.substring(index, index + 2).toInt(16).toByte()
        }
        return out
    }
}

