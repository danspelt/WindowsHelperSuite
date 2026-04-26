package com.voicecapture

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.lifecycle.viewmodel.compose.viewModel
import com.voicecapture.ui.VoiceCaptureScreen
import com.voicecapture.viewmodel.VoiceCaptureViewModel
import com.voicecapture.viewmodel.VoiceCaptureViewModelFactory

class MainActivity : ComponentActivity() {

    private var onPermissionResult: ((Boolean) -> Unit)? = null

    private val requestPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            onPermissionResult?.invoke(granted)
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        WindowCompat.setDecorFitsSystemWindows(window, false)

        val app = application as VoiceCaptureApplication
        val factory = VoiceCaptureViewModelFactory(application, app.settingsRepository)

        val initialGranted =
            ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) ==
                PackageManager.PERMISSION_GRANTED

        setContent {
            val viewModel: VoiceCaptureViewModel = viewModel(factory = factory)

            DisposableEffect(viewModel) {
                onPermissionResult = { granted ->
                    viewModel.setMicPermissionGranted(granted)
                    if (!granted) {
                        viewModel.stopListening()
                    } else {
                        viewModel.startListeningIfAllowedAndNotAlreadyStarted()
                    }
                }
                onDispose {
                    onPermissionResult = null
                }
            }

            LaunchedEffect(Unit) {
                viewModel.setMicPermissionGranted(initialGranted)
                if (initialGranted) {
                    viewModel.startListeningIfAllowedAndNotAlreadyStarted()
                }
            }

            VoiceCaptureScreen(
                viewModel = viewModel,
                onRequestMicPermission = {
                    requestPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
                },
            )
        }
    }
}
