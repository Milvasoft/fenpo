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

/**
 * Keystore keys that work only right after a strong biometric check (unlock.proto): every cipher
 * from here must go through `BiometricPrompt` with a `CryptoObject` before it can be used, and a
 * new fingerprint on the phone makes the key unusable (`KeyPermanentlyInvalidatedException`).
 */
interface BiometricKeys {
    /** A new key under [name], replacing an older one, and a cipher that seals with it after the check. */
    fun newSealingCipher(name: String): Cipher

    /**
     * A cipher that opens what [newSealingCipher] sealed with [iv], usable after the check.
     *
     * @throws android.security.keystore.KeyPermanentlyInvalidatedException after new biometrics.
     * @throws java.security.KeyException when there is no key under [name].
     */
    fun openingCipher(
        name: String,
        iv: ByteArray,
    ): Cipher

    fun delete(name: String)
}

@Singleton
internal class KeystoreBiometricKeys
    @Inject
    constructor() : BiometricKeys {
        override fun newSealingCipher(name: String): Cipher {
            val spec =
                KeyGenParameterSpec
                    .Builder(alias(name), KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(KEY_BITS)
                    .setUserAuthenticationRequired(true)
                    // Every use needs its own check; only class 3 (strong) biometrics, no device PIN.
                    .setUserAuthenticationParameters(0, KeyProperties.AUTH_BIOMETRIC_STRONG)
                    .setInvalidatedByBiometricEnrollment(true)
                    .build()
            val key =
                KeyGenerator
                    .getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE)
                    .apply { init(spec) }
                    .generateKey()
            return Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key) }
        }

        override fun openingCipher(
            name: String,
            iv: ByteArray,
        ): Cipher {
            val key =
                keyStore().getKey(alias(name), null) as? SecretKey
                    ?: throw java.security.KeyException("No key for $name")
            return Cipher
                .getInstance(
                    TRANSFORMATION,
                ).apply { init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_BITS, iv)) }
        }

        override fun delete(name: String) {
            runCatching { keyStore().deleteEntry(alias(name)) }
        }

        private fun keyStore() = KeyStore.getInstance(KEYSTORE).apply { load(null) }

        private fun alias(name: String) = "fenpo_biometric_$name"

        private companion object {
            const val KEYSTORE = "AndroidKeyStore"
            const val TRANSFORMATION = "AES/GCM/NoPadding"
            const val KEY_BITS = 256
            const val TAG_BITS = 128
        }
    }
