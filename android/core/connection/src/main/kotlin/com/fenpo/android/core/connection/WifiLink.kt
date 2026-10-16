package com.fenpo.android.core.connection

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.util.Log
import com.fenpo.android.core.data.pc.PairedPc
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.transport.Endpoint
import com.fenpo.android.core.transport.tls.EnvelopeConnection
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.isPinMismatch
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.Hello
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelChildren
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okio.ByteString.Companion.decodeHex
import kotlin.time.Duration
import kotlin.time.Duration.Companion.minutes
import kotlin.time.Duration.Companion.seconds

/**
 * The ④ control channel over Wi-Fi: one long-lived TLS 1.3 connection to the paired PC, both
 * certificates pinned (docs/ARCHITECTURE.md). Runs while a Wi-Fi or Ethernet network is up, next to
 * the BLE link; either path alone keeps the phone connected.
 *
 * Addresses come from pairing, from the PC's `DeviceState` and, when none answers, from DNS-SD.
 */
internal class WifiLink(
    context: Context,
    private val pc: PairedPc,
    private val connector: TlsConnector,
    private val localHello: Hello,
    private val status: ConnectionStatus,
    private val pairedPcRepository: PairedPcRepository,
    private val onSession: (PcSession?) -> Unit,
) {
    private val connectivity = context.getSystemService(ConnectivityManager::class.java)
    private val discovery = PcDiscovery(context)
    private val retry = Channel<Unit>(Channel.CONFLATED)

    /** Ask for a new attempt now, e.g. because the PC sent new addresses over Bluetooth. */
    fun retryNow() {
        retry.trySend(Unit)
    }

    /** Runs until cancelled. */
    suspend fun run() {
        localNetworks().collectLatest { network ->
            if (network == null) {
                status.update { it.copy(wifi = LinkState.OFF) }
                return@collectLatest
            }
            var backoff = MIN_BACKOFF
            while (true) {
                when (connectOnce(network)) {
                    Outcome.ENDED -> {
                        backoff = MIN_BACKOFF
                    }

                    Outcome.UNREACHABLE -> {
                        backoff = (backoff * 2).coerceAtMost(MAX_BACKOFF)
                    }

                    Outcome.NOT_PAIRED -> {
                        // The PC forgot this phone; only pairing again helps. Bluetooth tells the same story.
                        Log.w(TAG, "The PC does not know this phone any more")
                        return@collectLatest
                    }
                }
                withTimeoutOrNull(backoff) { retry.receive() }
            }
        }
    }

    private suspend fun connectOnce(network: Network): Outcome {
        status.update { it.copy(wifi = LinkState.CONNECTING) }
        val connection = open(network)
        if (connection == null) {
            status.update { it.copy(wifi = LinkState.OFF) }
            return Outcome.UNREACHABLE
        }
        val session =
            PcSession(
                LinkPath.WIFI,
                status,
                send = { body, replyTo -> connection.send(body, replyTo) },
                onAddresses = { addresses ->
                    pairedPcRepository.saveAddresses(addresses)
                },
            )
        try {
            status.update { it.copy(wifi = LinkState.CONNECTED) }
            connection.send(Envelope.Body.Hello(localHello))
            return coroutineScope {
                // A blocking socket read does not see cancellation; closing the socket ends it.
                val closer = launch { awaitCancellation() }
                closer.invokeOnCompletion { connection.close() }
                launch {
                    session.awaitHello()
                    onSession(session)
                    session.heartbeat.run()
                    Log.w(TAG, "PC stopped answering Ping over Wi-Fi, reconnecting")
                    connection.close()
                }
                val outcome = read(connection, session)
                coroutineContext.cancelChildren()
                outcome
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.i(TAG, "Wi-Fi link ended: ${e.message}")
            return Outcome.ENDED
        } finally {
            onSession(null)
            connection.close()
            status.update { it.copy(wifi = LinkState.OFF) }
        }
    }

    private suspend fun read(
        connection: EnvelopeConnection,
        session: PcSession,
    ): Outcome {
        while (true) {
            val envelope = connection.receive(READ_TIMEOUT)
            if (envelope == null) {
                Log.i(TAG, "The PC closed the Wi-Fi link")
                return Outcome.ENDED
            }
            val error = (envelope.body as? Envelope.Body.Error)?.value
            if (error?.code == ErrorCode.ERROR_CODE_NOT_PAIRED) return Outcome.NOT_PAIRED
            session.onEnvelope(envelope)
        }
    }

    /** Tries the stored addresses, then whatever DNS-SD finds; remembers the one that answered first. */
    private suspend fun open(network: Network): EnvelopeConnection? {
        val stored =
            pairedPcRepository.pairedPc
                .first()
                ?.addresses
                .orEmpty()
        val known = stored.mapNotNull(Endpoint::parse)
        tryEach(known, network)?.let { (endpoint, connection) ->
            remember(endpoint, stored)
            return connection
        }
        val discovered = discovery.find(pc.id, DISCOVERY_TIMEOUT).filter { it !in known }
        tryEach(discovered, network)?.let { (endpoint, connection) ->
            remember(endpoint, discovered.map { it.toString() } + stored)
            return connection
        }
        return null
    }

    private suspend fun tryEach(
        endpoints: List<Endpoint>,
        network: Network,
    ): Pair<Endpoint, EnvelopeConnection>? {
        val pin = pc.certificateSha256.decodeHex()
        for (endpoint in endpoints) {
            try {
                val connection = connector.connect(endpoint, pin, network.socketFactory)
                Log.i(TAG, "Wi-Fi link open to $endpoint (${connection.protocol})")
                return endpoint to connection
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // A different certificate means another device has this address now, not this PC.
                Log.d(TAG, "$endpoint: ${if (e.isPinMismatch()) "certificate does not match" else e.message}")
            }
        }
        return null
    }

    private suspend fun remember(
        working: Endpoint,
        candidates: List<String>,
    ) {
        val ordered = AddressOrder.withFirst(working.toString(), candidates)
        if (ordered != pairedPcRepository.pairedPc.first()?.addresses) pairedPcRepository.saveAddresses(ordered)
    }

    /** The current Wi-Fi or Ethernet network, also one without internet access, or `null`. */
    private fun localNetworks(): Flow<Network?> =
        callbackFlow {
            val available = LinkedHashSet<Network>()
            val callback =
                object : ConnectivityManager.NetworkCallback() {
                    override fun onAvailable(network: Network) {
                        available += network
                        trySend(network)
                    }

                    override fun onLost(network: Network) {
                        available -= network
                        trySend(available.lastOrNull())
                    }
                }
            val request =
                NetworkRequest
                    .Builder()
                    .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                    .addTransportType(NetworkCapabilities.TRANSPORT_ETHERNET)
                    .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                    .build()
            trySend(null)
            connectivity.registerNetworkCallback(request, callback)
            awaitClose { connectivity.unregisterNetworkCallback(callback) }
        }.distinctUntilChanged()

    private enum class Outcome { ENDED, UNREACHABLE, NOT_PAIRED }

    private companion object {
        const val TAG = "FenpoWifi"
        val MIN_BACKOFF = 5.seconds
        val MAX_BACKOFF = 5.minutes
        val DISCOVERY_TIMEOUT = 8.seconds

        // Pings every 30 s keep traffic flowing; this only catches a socket that went silent.
        val READ_TIMEOUT: Duration = 2.minutes
    }
}

/** Order of the PC's stored addresses. */
internal object AddressOrder {
    /** [working] first, then the other [candidates] without duplicates, at most [MAX] in all. */
    fun withFirst(
        working: String,
        candidates: List<String>,
    ): List<String> = (listOf(working) + candidates).distinct().take(MAX)

    private const val MAX = 8
}
