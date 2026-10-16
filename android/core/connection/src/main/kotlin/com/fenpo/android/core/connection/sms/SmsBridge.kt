package com.fenpo.android.core.connection.sms

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.database.ContentObserver
import android.database.Cursor
import android.os.Handler
import android.os.Looper
import android.provider.Telephony
import android.telephony.SmsManager
import android.util.Log
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.SmsConversation
import com.fenpo.protocol.v1.SmsConversations
import com.fenpo.protocol.v1.SmsMessage
import com.fenpo.protocol.v1.SmsMessages
import com.fenpo.protocol.v1.SmsMessagesRequest
import com.fenpo.protocol.v1.SmsSend
import com.fenpo.protocol.v1.SmsSendResult
import com.fenpo.protocol.v1.SmsStatus
import com.fenpo.protocol.v1.SmsSyncRequest
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

/**
 * SMS over the Fenpo channel (messages.proto, docs/ANDROID.md §7) for phones without MAP: answers
 * the PC's requests from Android's SMS provider, pushes new messages and status changes, and sends
 * texts. Runs only while "Sync messages" is on and the SMS permission is granted; the phone's
 * Hello offers CAPABILITY_MESSAGES under the same condition.
 */
@Singleton
class SmsBridge
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val settingsRepository: SettingsRepository,
    ) : PcFeature {
        private val known = LinkedHashMap<Long, Pair<Int, Boolean>>()

        override suspend fun run(): Unit =
            coroutineScope {
                launch { watch() }
                messenger.incoming.collect { envelope ->
                    if (!enabled()) return@collect
                    when (val body = envelope.body) {
                        is Envelope.Body.SmsSyncRequest -> reply(envelope.id, conversations(body.value))
                        is Envelope.Body.SmsMessagesRequest -> reply(envelope.id, messages(body.value))
                        is Envelope.Body.SmsSend -> reply(envelope.id, send(body.value))
                        else -> Unit
                    }
                }
            }

        private suspend fun reply(
            replyTo: Long,
            body: Envelope.Body,
        ) {
            messenger.send(body, Capability.CAPABILITY_MESSAGES, replyTo)
        }

        private suspend fun enabled(): Boolean =
            settingsRepository.settings
                .first()
                .features.messageSync && Sms.permitted(context)

        private suspend fun conversations(request: SmsSyncRequest): Envelope.Body {
            val limit = request.limit.takeIf { it > 0 } ?: DEFAULT_LIMIT
            val threads = LinkedHashMap<Long, SmsConversation>()
            withContext(Dispatchers.IO) {
                query(selection = null, args = null)?.use { cursor ->
                    var scanned = 0
                    while (cursor.moveToNext() && scanned++ < SCAN_LIMIT) {
                        val message = read(cursor)
                        val existing = threads[message.thread_id]
                        val unread = if (!message.outgoing && !message.read) 1 else 0
                        if (existing == null) {
                            if (threads.size >= limit) continue
                            threads[message.thread_id] =
                                SmsConversation(
                                    thread_id = message.thread_id,
                                    addresses = listOf(message.address),
                                    snippet = message.body.take(SNIPPET),
                                    last_at = message.at,
                                    unread = unread,
                                )
                        } else if (unread > 0) {
                            threads[message.thread_id] = existing.copy(unread = existing.unread + 1)
                        }
                    }
                }
            }
            return Envelope.Body.SmsConversations(SmsConversations(conversations = threads.values.toList()))
        }

        private suspend fun messages(request: SmsMessagesRequest): Envelope.Body {
            val limit = request.limit.takeIf { it > 0 } ?: DEFAULT_LIMIT
            val before = request.before?.toEpochMilli()
            val list =
                withContext(Dispatchers.IO) {
                    val selection =
                        "${Telephony.Sms.THREAD_ID} = ?" + if (before != null) " AND ${Telephony.Sms.DATE} < ?" else ""
                    val args = listOfNotNull(request.thread_id.toString(), before?.toString()).toTypedArray()
                    query(selection, args)
                        ?.use { cursor ->
                            buildList { while (size < limit && cursor.moveToNext()) add(read(cursor)) }
                        }.orEmpty()
                }
            return Envelope.Body.SmsMessages(SmsMessages(thread_id = request.thread_id, messages = list))
        }

        private fun send(request: SmsSend): Envelope.Body {
            val result =
                try {
                    val manager = context.getSystemService(SmsManager::class.java)
                    val parts = manager.divideMessage(request.body)
                    // Not the default SMS app: Android stores the sent message itself, and the provider watch reports it.
                    manager.sendMultipartTextMessage(request.address, null, parts, null, null)
                    Log.i(TAG, "Sending an SMS for the PC (${parts.size} part(s))")
                    SmsSendResult(request_id = request.request_id, accepted = true)
                } catch (e: Exception) {
                    Log.w(TAG, "SMS not sent: ${e.message}")
                    SmsSendResult(request_id = request.request_id, accepted = false, reason = e.message.orEmpty())
                }
            return Envelope.Body.SmsSendResult(result)
        }

        /** Pushes new messages and status changes as the SMS provider reports them. */
        private suspend fun watch() {
            val changes = Channel<Unit>(Channel.CONFLATED)
            val observer =
                object : ContentObserver(Handler(Looper.getMainLooper())) {
                    override fun onChange(selfChange: Boolean) {
                        changes.trySend(Unit)
                    }
                }
            if (!Sms.permitted(context)) return
            context.contentResolver.registerContentObserver(Telephony.Sms.CONTENT_URI, true, observer)
            try {
                withContext(Dispatchers.IO) { recent().forEach { remember(it) } }
                for (change in changes) {
                    if (!enabled()) continue
                    val changed = withContext(Dispatchers.IO) { recent().filter { remember(it) } }
                    for (message in changed.reversed()) {
                        messenger.send(Envelope.Body.SmsMessage(message), Capability.CAPABILITY_MESSAGES)
                    }
                    if (changed.isNotEmpty()) Log.d(TAG, "Pushed ${changed.size} SMS change(s)")
                }
            } finally {
                context.contentResolver.unregisterContentObserver(observer)
            }
        }

        /** `true` when [message] is new or its status or read state changed since last seen. */
        private fun remember(message: SmsMessage): Boolean {
            val state = message.status.value to message.read
            val previous = known.put(message.id, state)
            while (known.size > KNOWN) known.remove(known.keys.first())
            return previous != state
        }

        private fun recent(): List<SmsMessage> =
            context.contentResolver
                .query(Telephony.Sms.CONTENT_URI, COLUMNS, null, null, "${Telephony.Sms._ID} DESC")
                ?.use { cursor -> buildList { while (size < RECENT && cursor.moveToNext()) add(read(cursor)) } }
                .orEmpty()

        private fun query(
            selection: String?,
            args: Array<String>?,
        ): Cursor? =
            context.contentResolver.query(
                Telephony.Sms.CONTENT_URI,
                COLUMNS,
                selection,
                args,
                "${Telephony.Sms.DATE} DESC",
            )

        private fun read(cursor: Cursor): SmsMessage {
            val type = cursor.getInt(TYPE)
            return SmsMessage(
                id = cursor.getLong(ID),
                thread_id = cursor.getLong(THREAD),
                address = cursor.getString(ADDRESS).orEmpty(),
                body = cursor.getString(BODY).orEmpty(),
                at = Instant.ofEpochMilli(cursor.getLong(DATE)),
                outgoing = type != Telephony.Sms.MESSAGE_TYPE_INBOX,
                read = cursor.getInt(READ) != 0,
                status = Sms.status(type),
            )
        }

        private companion object {
            const val TAG = "FenpoSms"
            const val DEFAULT_LIMIT = 50
            const val SCAN_LIMIT = 3000
            const val SNIPPET = 200
            const val RECENT = 20
            const val KNOWN = 200
            val COLUMNS =
                arrayOf(
                    Telephony.Sms._ID,
                    Telephony.Sms.THREAD_ID,
                    Telephony.Sms.ADDRESS,
                    Telephony.Sms.BODY,
                    Telephony.Sms.DATE,
                    Telephony.Sms.TYPE,
                    Telephony.Sms.READ,
                )
            const val ID = 0
            const val THREAD = 1
            const val ADDRESS = 2
            const val BODY = 3
            const val DATE = 4
            const val TYPE = 5
            const val READ = 6
        }
    }

/** SMS permission and the provider's message types. */
object Sms {
    fun permitted(context: Context): Boolean =
        listOf(Manifest.permission.READ_SMS, Manifest.permission.SEND_SMS).all {
            ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
        }

    /** Android's `Telephony.Sms.TYPE` as the protocol's status. */
    fun status(type: Int): SmsStatus =
        when (type) {
            Telephony.Sms.MESSAGE_TYPE_INBOX -> SmsStatus.SMS_STATUS_RECEIVED
            Telephony.Sms.MESSAGE_TYPE_SENT -> SmsStatus.SMS_STATUS_SENT
            Telephony.Sms.MESSAGE_TYPE_OUTBOX, Telephony.Sms.MESSAGE_TYPE_QUEUED -> SmsStatus.SMS_STATUS_PENDING
            Telephony.Sms.MESSAGE_TYPE_FAILED -> SmsStatus.SMS_STATUS_FAILED
            else -> SmsStatus.SMS_STATUS_UNSPECIFIED
        }
}

@Module
@InstallIn(SingletonComponent::class)
internal abstract class SmsModule {
    @Binds
    @IntoSet
    abstract fun feature(bridge: SmsBridge): PcFeature
}
