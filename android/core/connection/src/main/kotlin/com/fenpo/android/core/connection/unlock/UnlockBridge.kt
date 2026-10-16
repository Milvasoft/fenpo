package com.fenpo.android.core.connection.unlock

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.hardware.biometrics.BiometricManager
import android.os.SystemClock
import android.security.keystore.KeyPermanentlyInvalidatedException
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.data.unlock.UnlockEnrollment
import com.fenpo.android.core.data.unlock.UnlockRepository
import com.fenpo.android.core.security.BiometricKeys
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.UnlockKey
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import okio.ByteString.Companion.toByteString
import java.security.GeneralSecurityException
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/** What the PC asked for and is waiting on (unlock.proto). */
data class UnlockAsk(
    val enrol: Boolean,
    val pcName: String,
    val requestId: String,
    val replyTo: Long,
    val at: Long = SystemClock.elapsedRealtime(),
) {
    /** The PC gives up after a minute. */
    val expired: Boolean get() = SystemClock.elapsedRealtime() - at > TIMEOUT_MS

    companion object {
        const val TIMEOUT_MS = 60_000L
    }
}

/**
 * Unlocking the PC with the fingerprint (unlock.proto). The PC keeps the Windows password sealed
 * with a 32-byte key that only the phone keeps, itself sealed by a Keystore key that works only
 * right after a strong biometric check ([BiometricKeys]). A request shows a notification; tapping
 * it opens [UnlockActivity], whose fingerprint check opens the key and sends it back. New
 * fingerprints on the phone make the key unusable; the PC is then told NOT_PAIRED and asks for the
 * password again.
 */
@Singleton
class UnlockBridge
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val repository: UnlockRepository,
        private val pairedPcRepository: PairedPcRepository,
        private val keys: BiometricKeys,
    ) : PcFeature {
        private val _pending = MutableStateFlow<UnlockAsk?>(null)

        /** The request the fingerprint screen answers. */
        val pending: StateFlow<UnlockAsk?> = _pending.asStateFlow()

        override suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.UnlockEnroll -> {
                        ask(UnlockAsk(enrol = true, pcName = body.value.pc_name, requestId = "", replyTo = envelope.id))
                    }

                    is Envelope.Body.UnlockRequest -> {
                        val ask =
                            UnlockAsk(
                                enrol = false,
                                pcName = body.value.pc_name,
                                requestId = body.value.request_id,
                                replyTo = envelope.id,
                            )
                        if (enrolledHere() ==
                            null
                        ) {
                            refuse(ask, ErrorCode.ERROR_CODE_NOT_PAIRED, "No key for this PC")
                        } else {
                            ask(ask)
                        }
                    }

                    is Envelope.Body.UnlockRevoke -> {
                        Log.i(TAG, "The PC turned unlocking off")
                        forget()
                    }

                    else -> {
                        Unit
                    }
                }
            }
        }

        private suspend fun ask(ask: UnlockAsk) {
            val biometrics = context.getSystemService(BiometricManager::class.java)
            if (biometrics.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG) !=
                BiometricManager.BIOMETRIC_SUCCESS
            ) {
                refuse(ask, ErrorCode.ERROR_CODE_UNSUPPORTED, "No strong biometrics on the phone")
                return
            }
            Log.i(TAG, if (ask.enrol) "The PC wants to set up unlocking" else "The PC wants to be unlocked")
            _pending.value = ask
            notifyUser(ask)
        }

        /**
         * The cipher the fingerprint check must approve for [ask]: a new key when setting up, the
         * stored one when unlocking. `null` after the answer went to the PC (no key, new fingerprints).
         */
        suspend fun cipherFor(ask: UnlockAsk): Cipher? {
            val pcId = pairedPcRepository.pairedPc.first()?.id
            if (pcId == null) {
                refuse(ask, ErrorCode.ERROR_CODE_NOT_PAIRED, "Not paired")
                return null
            }
            return try {
                if (ask.enrol) {
                    keys.newSealingCipher(KEY_NAME)
                } else {
                    val enrollment = enrolledHere() ?: throw java.security.KeyException("No key")
                    keys.openingCipher(KEY_NAME, enrollment.iv)
                }
            } catch (e: KeyPermanentlyInvalidatedException) {
                Log.i(TAG, "New fingerprints on the phone: the unlock key is gone")
                forget()
                refuse(ask, ErrorCode.ERROR_CODE_NOT_PAIRED, "Biometrics changed on the phone")
                null
            } catch (e: GeneralSecurityException) {
                Log.w(TAG, "Unlock key not usable: ${e.message}")
                refuse(ask, ErrorCode.ERROR_CODE_NOT_PAIRED, "No usable key")
                null
            }
        }

        /** After the fingerprint check: seals a new key or opens the stored one, and sends it. */
        suspend fun complete(
            ask: UnlockAsk,
            cipher: Cipher,
        ) {
            val key =
                try {
                    if (ask.enrol) {
                        val key = ByteArray(KEY_BYTES).also { SecureRandom().nextBytes(it) }
                        val sealed = cipher.doFinal(key)
                        val pcId =
                            pairedPcRepository.pairedPc
                                .first()
                                ?.id
                                .orEmpty()
                        repository.save(
                            UnlockEnrollment(pcId = pcId, pcName = ask.pcName, iv = cipher.iv, sealedKey = sealed),
                        )
                        key
                    } else {
                        cipher.doFinal(requireNotNull(enrolledHere()).sealedKey)
                    }
                } catch (e: GeneralSecurityException) {
                    Log.w(TAG, "Unlock key failed: ${e.message}")
                    refuse(ask, ErrorCode.ERROR_CODE_INTERNAL, "Key failed")
                    return
                }
            messenger.send(
                Envelope.Body.UnlockKey(UnlockKey(request_id = ask.requestId, key = key.toByteString())),
                Capability.CAPABILITY_UNLOCK,
                ask.replyTo,
            )
            key.fill(0)
            Log.i(TAG, if (ask.enrol) "Unlocking set up" else "Unlock approved")
            done(ask)
        }

        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

        /** [decline] on the bridge's own scope, for a screen that is closing. */
        fun declineSoon(ask: UnlockAsk) {
            scope.launch { decline(ask) }
        }

        /** The user cancelled the fingerprint check. */
        suspend fun decline(ask: UnlockAsk) {
            // Setting up made a new Keystore key already; without the sealed key it is of no use.
            if (ask.enrol) forget()
            refuse(ask, ErrorCode.ERROR_CODE_DECLINED, "Cancelled on the phone")
        }

        /** Settings > "Unlock your PC" > Remove, or the PC turning it off. */
        suspend fun forget() {
            keys.delete(KEY_NAME)
            repository.clear()
        }

        private suspend fun enrolledHere(): UnlockEnrollment? {
            val enrollment = repository.enrollment.first() ?: return null
            val pcId = pairedPcRepository.pairedPc.first()?.id
            return enrollment.takeIf { it.pcId == pcId }
        }

        private suspend fun refuse(
            ask: UnlockAsk,
            code: ErrorCode,
            message: String,
        ) {
            Log.i(TAG, "Unlock answered $code: $message")
            messenger.send(
                Envelope.Body.Error(Error(code = code, message = message)),
                Capability.CAPABILITY_UNLOCK,
                ask.replyTo,
            )
            done(ask)
        }

        private fun done(ask: UnlockAsk) {
            _pending.compareAndSet(ask, null)
            context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION_ID)
        }

        private fun notifyUser(ask: UnlockAsk) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.unlock_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ),
            )
            val open =
                PendingIntent.getActivity(
                    context,
                    NOTIFICATION_ID,
                    Intent(context, UnlockActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                )
            val title = if (ask.enrol) R.string.unlock_enrol_title else R.string.unlock_request_title
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(title, ask.pcName))
                    .setContentText(context.getString(R.string.unlock_tap))
                    .setPriority(NotificationCompat.PRIORITY_HIGH)
                    .setContentIntent(open)
                    .setFullScreenIntent(open, true)
                    .setAutoCancel(true)
                    .setTimeoutAfter(UnlockAsk.TIMEOUT_MS)
                    .build()
            manager.notify(NOTIFICATION_ID, notification)
        }

        private companion object {
            const val TAG = "FenpoUnlock"
            const val CHANNEL_ID = "unlock"
            const val NOTIFICATION_ID = 50
            const val KEY_NAME = "unlock"
            const val KEY_BYTES = 32
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class UnlockModule {
    @Binds
    @IntoSet
    abstract fun feature(bridge: UnlockBridge): PcFeature
}
