package com.voicecapture

import android.app.Application
import com.voicecapture.data.SettingsRepository

class VoiceCaptureApplication : Application() {
    lateinit var settingsRepository: SettingsRepository
        private set

    override fun onCreate() {
        super.onCreate()
        settingsRepository = SettingsRepository(this)
    }
}
