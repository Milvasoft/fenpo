package com.fenpo.android.core.connection.transfer

import android.Manifest
import android.app.DownloadManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * Progress and result notifications of file transfers (design: "Bildirimler ve Hızlı Ayarlar"):
 * "Sending to your PC" with a bar and "Cancel", then what arrived, opening the file or Downloads.
 */
internal class TransferNotifications(
    private val context: Context,
) {
    private val manager = context.getSystemService(NotificationManager::class.java)

    fun progress(
        transfer: ActiveTransfer,
        cancel: PendingIntent?,
    ) {
        if (!allowed()) return
        createChannel()
        val title = context.getString(if (transfer.incoming) R.string.transfer_receiving else R.string.transfer_sending)
        val percent = if (transfer.totalBytes > 0) (transfer.bytesDone * PERCENT / transfer.totalBytes).toInt() else 0
        val builder =
            NotificationCompat
                .Builder(context, CHANNEL_ID)
                .setSmallIcon(DesignR.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(describe(transfer.fileName, transfer.fileCount))
                .setProgress(PERCENT, percent, transfer.bytesDone == 0L)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setSilent(true)
                .setCategory(NotificationCompat.CATEGORY_PROGRESS)
        if (cancel != null) builder.addAction(0, context.getString(R.string.transfer_cancel), cancel)
        manager.notify(transfer.id.hashCode(), builder.build())
    }

    fun finished(record: TransferRecord) {
        if (!allowed()) return
        createChannel()
        val done = if (record.incoming) R.string.transfer_received else R.string.transfer_sent
        val title =
            context.getString(
                when (record.outcome) {
                    TransferOutcome.COMPLETED -> done
                    TransferOutcome.DECLINED -> R.string.transfer_declined
                    TransferOutcome.CANCELLED -> R.string.transfer_cancelled
                    TransferOutcome.FAILED -> R.string.transfer_failed
                },
            )
        val builder =
            NotificationCompat
                .Builder(context, CHANNEL_ID)
                .setSmallIcon(DesignR.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(describe(record.fileName, record.fileCount))
                .setAutoCancel(true)
        if (record.outcome == TransferOutcome.COMPLETED && record.incoming) builder.setContentIntent(open(record))
        manager.notify(record.id.hashCode(), builder.build())
    }

    /** One file opens in its app; several open Downloads. */
    private fun open(record: TransferRecord): PendingIntent {
        val uri = record.uri
        val intent =
            if (record.fileCount == 1 && uri != null) {
                Intent(Intent.ACTION_VIEW, uri.toUri()).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            } else {
                Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)
            }.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        return PendingIntent.getActivity(context, record.id.hashCode(), intent, PendingIntent.FLAG_IMMUTABLE)
    }

    private fun describe(
        name: String,
        count: Int,
    ): String =
        if (count == 1) {
            name
        } else {
            context.resources.getQuantityString(R.plurals.transfer_files, count, name, count - 1)
        }

    private fun allowed(): Boolean =
        ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED

    private fun createChannel() {
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                context.getString(R.string.transfer_channel_name),
                NotificationManager.IMPORTANCE_LOW,
            ),
        )
    }

    private companion object {
        const val CHANNEL_ID = "transfers"
        const val PERCENT = 100
    }
}
