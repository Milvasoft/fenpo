package com.fenpo.android.core.security

import com.fenpo.android.core.transport.tls.TlsIdentity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton

/** Hands out this phone's TLS identity, creating the Keystore key on first use. */
interface IdentityProvider {
    /** The identity for [deviceId]; the device id goes into the certificate subject. */
    suspend fun identity(deviceId: String): TlsIdentity
}

@Singleton
internal class KeystoreIdentityProvider
    @Inject
    constructor() : IdentityProvider {
        private val lock = Mutex()
        private var cached: TlsIdentity? = null

        override suspend fun identity(deviceId: String): TlsIdentity =
            lock.withLock {
                cached
                    ?: withContext(Dispatchers.IO) { KeystoreTlsIdentity.loadOrCreate(deviceId) }.also { cached = it }
            }
    }
