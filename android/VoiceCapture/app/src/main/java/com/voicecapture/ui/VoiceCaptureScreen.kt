package com.voicecapture.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.activity.ComponentActivity
import android.view.WindowManager
import com.voicecapture.viewmodel.VoiceCaptureViewModel

private val CaptionYellow = Color(0xFFFFE082)
private val Charcoal = Color(0xFF121212)

@Composable
fun VoiceCaptureScreen(
    viewModel: VoiceCaptureViewModel,
    onRequestMicPermission: () -> Unit,
) {
    val state by viewModel.uiState.collectAsState()
    val scroll = rememberScrollState()

    val activity = LocalContext.current as ComponentActivity
    DisposableEffect(state.isListening, state.keepScreenAwake) {
        val window = activity.window
        if (state.isListening && state.keepScreenAwake) {
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
        onDispose {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    MaterialTheme(
        colorScheme =
            darkColorScheme(
                primary = CaptionYellow,
                onPrimary = Color.Black,
                surface = Charcoal,
                onSurface = Color.White,
                background = Color.Black,
                onBackground = Color.White,
            ),
    ) {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = Color.Black,
        ) {
            Column(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .background(Color.Black)
                        .padding(horizontal = 16.dp, vertical = 12.dp),
            ) {
                Text(
                    text = "Live Captions",
                    color = CaptionYellow,
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                )
                Text(
                    text = state.statusText,
                    color = Color(0xFFBBBBBB),
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.padding(top = 4.dp, bottom = 8.dp),
                )

                if (!state.micPermissionGranted) {
                    Text(
                        text =
                            "Microphone access is needed so the app can turn speech into large on-screen text.",
                        color = Color(0xFFCCCCCC),
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(bottom = 8.dp),
                    )
                    OutlinedButton(
                        onClick = onRequestMicPermission,
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("Allow microphone")
                    }
                    Spacer(modifier = Modifier.height(12.dp))
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text("1 Sentence mode", color = Color(0xFFCCCCCC))
                    Switch(
                        checked = state.singleSentenceMode,
                        onCheckedChange = { viewModel.setSingleSentenceMode(it) },
                    )
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text("Append mode", color = Color(0xFFCCCCCC), modifier = Modifier.alpha(if (state.singleSentenceMode) 0.5f else 1f))
                    Switch(
                        checked = state.appendMode,
                        onCheckedChange = { viewModel.setAppendMode(it) },
                        enabled = !state.singleSentenceMode,
                    )
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text("Keep screen awake", color = Color(0xFFCCCCCC))
                    Switch(
                        checked = state.keepScreenAwake,
                        onCheckedChange = { viewModel.setKeepScreenAwake(it) },
                    )
                }
                Text(
                    text = "Caption size (${state.fontScaleSp.toInt()} sp)",
                    color = Color(0xFFAAAAAA),
                    style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.padding(top = 8.dp),
                )
                Slider(
                    value = state.fontScaleSp,
                    onValueChange = { viewModel.setFontScaleSp(it) },
                    valueRange = 32f..96f,
                    modifier = Modifier.fillMaxWidth(),
                )

                Column(
                    modifier =
                        Modifier
                            .weight(1f)
                            .fillMaxWidth()
                            .verticalScroll(scroll)
                            .padding(vertical = 8.dp),
                    verticalArrangement = Arrangement.Center,
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(
                        text = state.displayText,
                        color = Color.White,
                        fontSize = state.fontScaleSp.sp,
                        fontWeight = FontWeight.Bold,
                        textAlign = TextAlign.Center,
                        lineHeight = (state.fontScaleSp + 12f).sp,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    CaptionButton(
                        text = "Start",
                        onClick = { viewModel.startListening() },
                        enabled = state.micPermissionGranted && !state.isListening,
                        modifier = Modifier.weight(1f),
                    )
                    CaptionButton(
                        text = "Stop",
                        onClick = { viewModel.stopListening() },
                        enabled = state.isListening,
                        modifier = Modifier.weight(1f),
                    )
                    CaptionButton(
                        text = "Clear",
                        onClick = { viewModel.clearText() },
                        modifier = Modifier.weight(1f),
                    )
                    CaptionButton(
                        text = "Copy",
                        onClick = { viewModel.copyTranscript() },
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
    }
}

@Composable
private fun CaptionButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    Button(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier.height(52.dp),
        colors =
            ButtonDefaults.buttonColors(
                containerColor = Color(0xFF2A2A2A),
                contentColor = Color.White,
                disabledContainerColor = Color(0xFF1A1A1A),
                disabledContentColor = Color(0xFF666666),
            ),
    ) {
        Text(text, fontWeight = FontWeight.SemiBold)
    }
}
