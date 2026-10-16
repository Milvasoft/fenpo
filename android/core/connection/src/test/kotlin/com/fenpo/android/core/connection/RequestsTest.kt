package com.fenpo.android.core.connection

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.files.PcPaths
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.FolderEntry
import com.fenpo.protocol.v1.FolderListRequest
import com.fenpo.protocol.v1.FolderListing
import com.fenpo.protocol.v1.Hello
import kotlinx.coroutines.async
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.seconds

/** Requests that wait for the PC's answer (files.proto) and the shared folders' paths. */
@RunWith(AndroidJUnit4::class)
class RequestsTest {
    private val messenger = PcMessenger()
    private var nextId = 10L
    private val listing =
        Envelope.Body.FolderListing(
            FolderListing(path = "", entries = listOf(FolderEntry(name = "Documents", path = "Documents"))),
        )

    /** A Wi-Fi session; [answer] plays the PC for each request sent on it. */
    private suspend fun connect(answer: (Long) -> Unit = {}) {
        val session =
            PcSession(LinkPath.WIFI, ConnectionStatus(), { _, _ ->
                val id = nextId++
                answer(id)
                id
            }, {})
        session.onEnvelope(
            Envelope(
                id = 1,
                body = Envelope.Body.Hello(Hello(capabilities = listOf(Capability.CAPABILITY_FILE_PROVIDER))),
            ),
        )
        messenger.onSession(LinkPath.WIFI, session)
    }

    private val request = Envelope.Body.FolderListRequest(FolderListRequest(path = ""))

    @Test
    fun answerArrivingAfterTheSendIsReturned() =
        runTest {
            connect()
            val pending = async { messenger.request(request, Capability.CAPABILITY_FILE_PROVIDER, 5.seconds) }
            runCurrent()
            messenger.deliver(Envelope(id = 2, reply_to = 10, body = listing), LinkPath.WIFI)
            assertEquals(listing, pending.await()?.body)
        }

    @Test
    fun answerArrivingBeforeTheSendReturnedIsNotLost() =
        runTest {
            // The PC's answer is delivered while the request is still being written.
            connect { id -> messenger.deliver(Envelope(id = 2, reply_to = id, body = listing), LinkPath.WIFI) }
            assertEquals(listing, messenger.request(request, Capability.CAPABILITY_FILE_PROVIDER, 5.seconds)?.body)
        }

    @Test
    fun answersDoNotReachFeaturesButOtherRepliesDo() =
        runTest {
            connect()
            val seen = mutableListOf<Envelope>()
            val collector = backgroundScope.async { messenger.incoming.collect { seen += it } }
            runCurrent()
            val pending = async { messenger.request(request, Capability.CAPABILITY_FILE_PROVIDER, 5.seconds) }
            runCurrent()
            messenger.deliver(Envelope(id = 2, reply_to = 10, body = listing), LinkPath.WIFI)
            pending.await()
            // An offer answering a FileFetch carries reply_to too, but nobody waits for it here.
            messenger.deliver(Envelope(id = 3, reply_to = 99, body = listing), LinkPath.WIFI)
            runCurrent()
            assertEquals(listOf(3L), seen.map { it.id })
            collector.cancel()
        }

    @Test
    fun noAnswerWithoutWifiOrCapability() =
        runTest {
            assertNull(messenger.request(request, Capability.CAPABILITY_FILE_PROVIDER, 1.seconds))
            connect()
            assertNull(messenger.request(request, Capability.CAPABILITY_PHOTOS, 1.seconds))
        }

    @Test
    fun paths() {
        assertEquals("", PcPaths.parent("Documents"))
        assertEquals("Documents/Taxes", PcPaths.parent("Documents/Taxes/2026.pdf"))
        assertEquals("Documents/Taxes", PcPaths.child("Documents", "Taxes"))
        assertEquals("Documents", PcPaths.child("", "Documents"))
        assertEquals("2026.pdf", PcPaths.name("Documents/Taxes/2026.pdf"))
        assertTrue(PcPaths.isInside("Documents/Taxes", ""))
        assertTrue(PcPaths.isInside("Documents/Taxes/2026.pdf", "Documents"))
        assertFalse(PcPaths.isInside("Documents2/a", "Documents"))
        assertFalse(PcPaths.isInside("Documents", "Documents"))
    }
}
