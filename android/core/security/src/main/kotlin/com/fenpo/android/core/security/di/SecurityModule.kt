package com.fenpo.android.core.security.di

import com.fenpo.android.core.security.BiometricKeys
import com.fenpo.android.core.security.IdentityProvider
import com.fenpo.android.core.security.KeystoreBiometricKeys
import com.fenpo.android.core.security.KeystoreIdentityProvider
import com.fenpo.android.core.security.KeystoreSecretBox
import com.fenpo.android.core.security.SecretBox
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

@Module
@InstallIn(SingletonComponent::class)
internal abstract class SecurityModule {
    @Binds
    abstract fun identityProvider(impl: KeystoreIdentityProvider): IdentityProvider

    @Binds
    abstract fun secretBox(impl: KeystoreSecretBox): SecretBox

    @Binds
    abstract fun biometricKeys(impl: KeystoreBiometricKeys): BiometricKeys
}
