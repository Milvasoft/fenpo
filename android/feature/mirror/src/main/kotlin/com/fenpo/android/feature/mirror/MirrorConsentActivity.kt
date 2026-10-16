package com.fenpo.android.feature.mirror

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

/**
 * Invisible step between the "Share screen" notification and Android's own consent dialog. When
 * the PC also wants the phone's sound, the microphone permission comes first: Android's playback
 * capture runs under it.
 */
@AndroidEntryPoint
class MirrorConsentActivity : ComponentActivity() {
    @Inject lateinit var coordinator: MirrorCoordinator

    private val audioPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { askForCapture() }

    private val capture =
        registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
            val data = result.data
            if (result.resultCode == RESULT_OK && data != null) MirrorService.start(this, result.resultCode, data)
            finish()
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState != null) return
        val wantsAudio = coordinator.request?.audio == true
        val hasAudio =
            ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) ==
                PackageManager.PERMISSION_GRANTED
        if (wantsAudio && !hasAudio) audioPermission.launch(Manifest.permission.RECORD_AUDIO) else askForCapture()
    }

    private fun askForCapture() {
        capture.launch(getSystemService(MediaProjectionManager::class.java).createScreenCaptureIntent())
    }

    companion object {
        fun intent(context: Context): Intent =
            Intent(context, MirrorConsentActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}
