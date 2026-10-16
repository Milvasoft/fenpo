package com.fenpo.android.core.connection.unlock

import android.hardware.biometrics.BiometricManager
import android.hardware.biometrics.BiometricPrompt
import android.os.Bundle
import android.os.CancellationSignal
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import com.fenpo.android.core.connection.R
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.launch

/**
 * The fingerprint check for unlocking the PC (unlock.proto), opened from the request's
 * notification: a see-through activity that only shows the system's biometric prompt. The prompt
 * approves the Keystore cipher itself, so the key opens only after a real strong check.
 */
class UnlockActivity : ComponentActivity() {
    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun unlockBridge(): UnlockBridge
    }

    private val bridge by lazy {
        EntryPointAccessors.fromApplication(applicationContext, Dependencies::class.java).unlockBridge()
    }
    private val cancel = CancellationSignal()

    /** One answer per request: closing the prompt after success also reports an error. */
    private var answered = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState != null) return
        val ask = bridge.pending.value
        if (ask == null || ask.expired) {
            finish()
            return
        }
        lifecycleScope.launch {
            val cipher = bridge.cipherFor(ask)
            if (cipher == null) {
                finish()
                return@launch
            }
            val title = if (ask.enrol) R.string.unlock_enrol_title else R.string.unlock_request_title
            val subtitle = if (ask.enrol) R.string.unlock_enrol_subtitle else R.string.unlock_request_subtitle
            BiometricPrompt
                .Builder(this@UnlockActivity)
                .setTitle(getString(title, ask.pcName))
                .setSubtitle(getString(subtitle, ask.pcName))
                .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
                .setNegativeButton(getString(R.string.unlock_cancel), mainExecutor) { _, _ -> declineAndFinish(ask) }
                .build()
                .authenticate(
                    BiometricPrompt.CryptoObject(cipher),
                    cancel,
                    mainExecutor,
                    object : BiometricPrompt.AuthenticationCallback() {
                        override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                            val approved = result.cryptoObject?.cipher ?: return declineAndFinish(ask)
                            if (answered) return
                            answered = true
                            lifecycleScope.launch {
                                bridge.complete(ask, approved)
                                finish()
                            }
                        }

                        // Cancelled, timed out or locked out after too many tries.
                        override fun onAuthenticationError(
                            errorCode: Int,
                            errString: CharSequence,
                        ) = declineAndFinish(ask)
                    },
                )
        }
    }

    private fun declineAndFinish(ask: UnlockAsk) {
        if (answered) return
        answered = true
        // The activity may already be going away (Back): the answer must still reach the PC.
        bridge.declineSoon(ask)
        finish()
    }

    override fun onDestroy() {
        if (isFinishing) cancel.cancel()
        super.onDestroy()
    }
}
