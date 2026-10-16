package com.fenpo.android.core.data.transfer

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

/** How a transfer ended. */
enum class TransferOutcome { COMPLETED, FAILED, CANCELLED, DECLINED }

/**
 * One finished transfer in "Recent transfers" and on the Transfers tab.
 *
 * @property fileName Name of the first file.
 * @property fileCount Number of files in the transfer.
 * @property uri Content URI of the first received file, to open it; `null` for sent files.
 */
data class TransferRecord(
    val id: String,
    val fileName: String,
    val fileCount: Int,
    val incoming: Boolean,
    val at: Instant,
    val bytes: Long,
    val outcome: TransferOutcome,
    val uri: String? = null,
)

/** The last transfers, newest first. */
interface TransferHistory {
    val records: Flow<List<TransferRecord>>

    /** Adds [record] at the top and keeps the list short. */
    suspend fun add(record: TransferRecord)
}

@Singleton
internal class DataStoreTransferHistory
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
    ) : TransferHistory {
        override val records: Flow<List<TransferRecord>> = dataStore.data.map { decode(it[KEY]) }

        override suspend fun add(record: TransferRecord) {
            dataStore.edit { it[KEY] = encode((listOf(record) + decode(it[KEY])).take(MAX_RECORDS)) }
        }

        internal companion object {
            val KEY = stringPreferencesKey("transfer_history")
            const val MAX_RECORDS = 50

            fun encode(records: List<TransferRecord>): String =
                JSONArray(
                    records.map { r ->
                        JSONObject()
                            .put("id", r.id)
                            .put("name", r.fileName)
                            .put("count", r.fileCount)
                            .put("in", r.incoming)
                            .put("at", r.at.toEpochMilli())
                            .put("bytes", r.bytes)
                            .put("outcome", r.outcome.name)
                            .put("uri", r.uri ?: JSONObject.NULL)
                    },
                ).toString()

            /** Unreadable or unknown entries are dropped rather than failing the whole list. */
            fun decode(text: String?): List<TransferRecord> {
                if (text.isNullOrEmpty()) return emptyList()
                val array = runCatching { JSONArray(text) }.getOrNull() ?: return emptyList()
                return (0 until array.length()).mapNotNull { i ->
                    runCatching {
                        val o = array.getJSONObject(i)
                        TransferRecord(
                            id = o.getString("id"),
                            fileName = o.getString("name"),
                            fileCount = o.getInt("count"),
                            incoming = o.getBoolean("in"),
                            at = Instant.ofEpochMilli(o.getLong("at")),
                            bytes = o.getLong("bytes"),
                            outcome = TransferOutcome.valueOf(o.getString("outcome")),
                            uri = if (o.isNull("uri")) null else o.getString("uri"),
                        )
                    }.getOrNull()
                }
            }
        }
    }
