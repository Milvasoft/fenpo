package com.fenpo.android.core.connection

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Build
import android.util.Log
import com.fenpo.android.core.transport.Endpoint
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.coroutines.resume
import kotlin.time.Duration

/**
 * Finds the paired PC on the local network through DNS-SD: the PC registers `_fenpo._tcp` with its
 * id in the TXT record (docs/ARCHITECTURE.md, "Keşif"). Used when none of the stored addresses
 * answers, e.g. after the PC got a new address from the router.
 */
internal class PcDiscovery(
    context: Context,
) {
    private val nsd = context.getSystemService(NsdManager::class.java)

    /**
     * Browses for up to [timeout] and returns the endpoints of the service whose TXT `id` is
     * [pcId], IPv4 first; empty if it did not show up.
     */
    suspend fun find(
        pcId: String,
        timeout: Duration,
    ): List<Endpoint> {
        val found = Channel<NsdServiceInfo>(Channel.UNLIMITED)
        val listener =
            object : NsdManager.DiscoveryListener {
                override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                    found.trySend(serviceInfo)
                }

                override fun onStartDiscoveryFailed(
                    serviceType: String,
                    errorCode: Int,
                ) {
                    Log.w(TAG, "DNS-SD browse failed: $errorCode")
                    found.close()
                }

                override fun onServiceLost(serviceInfo: NsdServiceInfo) = Unit

                override fun onDiscoveryStarted(serviceType: String) = Unit

                override fun onDiscoveryStopped(serviceType: String) = Unit

                override fun onStopDiscoveryFailed(
                    serviceType: String,
                    errorCode: Int,
                ) = Unit
            }
        nsd.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, listener)
        try {
            return withTimeoutOrNull(timeout) {
                // Resolve one at a time: before Android 14 a second resolve fails while one runs.
                for (service in found) {
                    val resolved = resolve(service) ?: continue
                    val id = resolved.attributes[TXT_ID]?.decodeToString()
                    if (id != pcId) continue
                    val endpoints = resolved.addresses().map { Endpoint(it.hostAddress.orEmpty(), resolved.port) }
                    Log.i(TAG, "Found the PC over DNS-SD: ${endpoints.size} address(es)")
                    return@withTimeoutOrNull endpoints
                }
                null
            }.orEmpty()
        } finally {
            runCatching { nsd.stopServiceDiscovery(listener) }
        }
    }

    @Suppress("DEPRECATION") // registerServiceInfoCallback is API 34; this works on 31 and later.
    private suspend fun resolve(service: NsdServiceInfo): NsdServiceInfo? =
        suspendCancellableCoroutine { continuation ->
            nsd.resolveService(
                service,
                object : NsdManager.ResolveListener {
                    override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                        continuation.resume(serviceInfo)
                    }

                    override fun onResolveFailed(
                        serviceInfo: NsdServiceInfo,
                        errorCode: Int,
                    ) {
                        Log.d(TAG, "DNS-SD resolve failed: $errorCode")
                        continuation.resume(null)
                    }
                },
            )
        }

    private fun NsdServiceInfo.addresses(): List<InetAddress> {
        val all =
            if (Build.VERSION.SDK_INT >= ANDROID_14) {
                hostAddresses
            } else {
                @Suppress("DEPRECATION")
                listOfNotNull(host)
            }
        return all.sortedBy { if (it is Inet4Address) 0 else 1 }
    }

    private companion object {
        const val TAG = "FenpoWifi"
        const val SERVICE_TYPE = "_fenpo._tcp"
        const val TXT_ID = "id"
        const val ANDROID_14 = 34
    }
}
