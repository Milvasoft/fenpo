package com.fenpo.android.core.connection

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.clipboard.ClipboardSendResult
import com.fenpo.android.core.connection.clipboard.ClipboardSync
import com.fenpo.android.core.connection.remote.WebLink
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.ClipboardItem
import com.fenpo.protocol.v1.ClipboardRequest
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.LockPc
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
class FeatureMessagesTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val clipboard = context.getSystemService(ClipboardManager::class.java)
    private val messenger = PcMessenger()
    private val sent = mutableListOf<Pair<Envelope.Body, Long>>()
    private val settings = FakeSettings()

    /** A Wi-Fi session whose PC offered [capabilities] in its Hello. */
    private suspend fun connect(vararg capabilities: Capability) {
        val session = PcSession(LinkPath.WIFI, ConnectionStatus(), { body, replyTo -> sent += body to replyTo }, {})
        session.onEnvelope(Envelope(id = 1, body = Envelope.Body.Hello(Hello(capabilities = capabilities.toList()))))
        messenger.onSession(LinkPath.WIFI, session)
    }

    @Test
    fun sendsOnlyWhatThePcOffers() =
        runTest {
            assertFalse(messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC))

            connect(Capability.CAPABILITY_DEVICE_STATE)
            assertFalse(messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC))

            connect(Capability.CAPABILITY_LOCK_PC)
            assertTrue(messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC))
            assertEquals(listOf<Pair<Envelope.Body, Long>>(Envelope.Body.LockPc(LockPc()) to 0L), sent)
        }

    @Test
    fun textFromThePcGoesToTheClipboard() =
        runTest {
            val sync = ClipboardSync(context, messenger, settings)
            val job = launch { sync.run() }
            runCurrent()
            val item = ClipboardItem(item_id = "a", content = ClipboardItem.Content.Text("docker compose up -d"))

            messenger.deliver(Envelope(id = 5, body = Envelope.Body.Clipboard(item)))
            runCurrent()

            assertEquals(
                "docker compose up -d",
                clipboard.primaryClip
                    ?.getItemAt(0)
                    ?.text
                    .toString(),
            )
            assertEquals("docker compose up -d", sync.fromPc.value)
            job.cancel()
        }

    @Test
    fun clipboardRequestIsDeclinedBecauseAndroidForbidsBackgroundReads() =
        runTest {
            connect(Capability.CAPABILITY_CLIPBOARD)
            val sync = ClipboardSync(context, messenger, settings)
            val job = launch { sync.run() }
            runCurrent()

            messenger.deliver(Envelope(id = 9, body = Envelope.Body.ClipboardRequest(ClipboardRequest())))
            runCurrent()

            val (body, replyTo) = sent.single()
            assertEquals(ErrorCode.ERROR_CODE_UNSUPPORTED, assertIs<Envelope.Body.Error>(body).value.code)
            assertEquals(9L, replyTo)
            job.cancel()
        }

    @Test
    fun sendsTextAndLinksWithTheirKind() =
        runTest {
            val sync = ClipboardSync(context, messenger, settings)
            assertEquals(ClipboardSendResult.NOT_CONNECTED, sync.sendCurrent())

            connect(Capability.CAPABILITY_CLIPBOARD)
            clipboard.setPrimaryClip(ClipData.newPlainText("t", ""))
            assertEquals(ClipboardSendResult.EMPTY, sync.sendCurrent())

            clipboard.setPrimaryClip(ClipData.newPlainText("t", "hello"))
            assertEquals(ClipboardSendResult.SENT, sync.sendCurrent())
            clipboard.setPrimaryClip(ClipData.newPlainText("t", "https://fenpo.app/docs"))
            assertEquals(ClipboardSendResult.SENT, sync.sendCurrent())

            val contents = sent.map { (assertIs<Envelope.Body.Clipboard>(it.first)).value.content }
            assertEquals(
                listOf(ClipboardItem.Content.Text("hello"), ClipboardItem.Content.Url("https://fenpo.app/docs")),
                contents,
            )
            assertEquals("https://fenpo.app/docs", sync.toPc.value)
        }

    @Test
    fun ignoresItsOwnItemComingBack() =
        runTest {
            connect(Capability.CAPABILITY_CLIPBOARD)
            val sync = ClipboardSync(context, messenger, settings)
            clipboard.setPrimaryClip(ClipData.newPlainText("t", "mine"))
            sync.sendCurrent()
            val echoed = assertIs<Envelope.Body.Clipboard>(sent.single().first)
            val job = launch { sync.run() }
            runCurrent()

            messenger.deliver(Envelope(id = 3, body = echoed))
            runCurrent()

            assertNull(sync.fromPc.value)
            job.cancel()
        }

    @Test
    fun clipboardSharingOffStopsBothDirections() =
        runTest {
            connect(Capability.CAPABILITY_CLIPBOARD)
            settings.update { it.copy(features = it.features.copy(clipboardSharing = false)) }
            val sync = ClipboardSync(context, messenger, settings)
            clipboard.setPrimaryClip(ClipData.newPlainText("t", "before"))
            val job = launch { sync.run() }
            runCurrent()

            assertEquals(ClipboardSendResult.TURNED_OFF, sync.sendCurrent())
            messenger.deliver(
                Envelope(
                    id = 4,
                    body =
                        Envelope.Body.Clipboard(
                            ClipboardItem(item_id = "x", content = ClipboardItem.Content.Text("from pc")),
                        ),
                ),
            )
            runCurrent()

            assertEquals(
                "before",
                clipboard.primaryClip
                    ?.getItemAt(0)
                    ?.text
                    .toString(),
            )
            job.cancel()
        }

    @Test
    fun sensitiveItemsAreMarkedAndNotPreviewed() =
        runTest {
            connect(Capability.CAPABILITY_CLIPBOARD)
            val sync = ClipboardSync(context, messenger, settings)
            val copied = ClipData.newPlainText("t", "hunter2")
            copied.description.extras =
                android.os.PersistableBundle().apply { putBoolean("android.content.extra.IS_SENSITIVE", true) }
            clipboard.setPrimaryClip(copied)

            assertEquals(ClipboardSendResult.SENT, sync.sendCurrent())
            assertTrue(assertIs<Envelope.Body.Clipboard>(sent.single().first).value.sensitive)
            assertEquals(context.getString(R.string.clipboard_sensitive), sync.toPc.value)

            val job = launch { sync.run() }
            runCurrent()
            val fromPc = ClipboardItem(item_id = "p", sensitive = true, content = ClipboardItem.Content.Text("s3cret"))
            messenger.deliver(Envelope(id = 6, body = Envelope.Body.Clipboard(fromPc)))
            runCurrent()

            assertEquals(
                "s3cret",
                clipboard.primaryClip
                    ?.getItemAt(0)
                    ?.text
                    .toString(),
            )
            assertEquals(context.getString(R.string.clipboard_sensitive), sync.fromPc.value)
            job.cancel()
        }

    @Test
    fun rssiIsReportedOnChangeOrEveryHalfMinute() {
        val gate = RssiGate()
        assertTrue(gate.accept(-50, 0))
        assertFalse(gate.accept(-52, 5_000))
        assertTrue(gate.accept(-55, 10_000))
        assertFalse(gate.accept(-55, 35_000))
        assertTrue(gate.accept(-55, 40_000))
    }

    @Test
    fun findsWebLinksOnly() {
        assertEquals("https://fenpo.app/a?b=1", WebLink.firstIn("Fenpo docs https://fenpo.app/a?b=1."))
        assertEquals("http://192.168.1.14:8080/x", WebLink.firstIn("http://192.168.1.14:8080/x"))
        assertNull(WebLink.firstIn("no link here"))
        assertFalse(WebLink.isWebLink("intent://scan/#Intent;scheme=zxing;end"))
        assertFalse(WebLink.isWebLink("file:///sdcard/secret.txt"))
        assertFalse(WebLink.isWebLink("https://"))
    }
}
