package com.fenpo.android.core.security

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.inject.Inject
import javax.inject.Singleton

/** Encrypts small secrets (the BLE channel key) before they are written to app storage. */
interface SecretBox {
    /** Returns `[12 byte IV][ciphertext and tag]`. */
    fun seal(plaintext: ByteArray): ByteArray

    /** Reverses [seal]; throws if the data was changed or the key is gone. */
    fun open(sealed: ByteArray): ByteArray
}

/** [SecretBox] with an AES-256-GCM key held in the Android Keystore. */
@Singleton
internal class KeystoreSecretBox
    @Inject
    constructor() : SecretBox {
        override fun seal(plaintext: ByteArray): ByteArray {
            val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key()) }
            return cipher.iv + cipher.doFinal(plaintext)
        }

        override fun open(sealed: ByteArray): ByteArray {
            require(sealed.size > IV_BYTES) { "Sealed data too short" }
            val cipher =
                Cipher.getInstance(TRANSFORMATION).apply {
                    init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(TAG_BITS, sealed, 0, IV_BYTES))
                }
            return cipher.doFinal(sealed, IV_BYTES, sealed.size - IV_BYTES)
        }

        private fun key(): SecretKey {
            val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
            (keyStore.getKey(ALIAS, null) as? SecretKey)?.let { return it }
            val spec =
                KeyGenParameterSpec
                    .Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(KEY_BITS)
                    .build()
            return KeyGenerator
                .getInstance(
                    KeyProperties.KEY_ALGORITHM_AES,
                    KEYSTORE,
                ).apply { init(spec) }
                .generateKey()
        }

        private companion object {
            const val KEYSTORE = "AndroidKeyStore"
            const val ALIAS = "fenpo.secrets.v1"
            const val TRANSFORMATION = "AES/GCM/NoPadding"
            const val IV_BYTES = 12
            const val TAG_BITS = 128
            const val KEY_BITS = 256
        }
    }
