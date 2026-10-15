package com.fenpo.android.feature.home

import java.time.Instant

/**
 * Everything the home screen shows.
 *
 * @property pc The paired PC, or `null` before pairing.
 * @property phoneClipboardPreview Start of the text that "Send clipboard" would send, if known.
 * @property pcClipboardPreview Start of the PC's latest clipboard item.
 * @property recentPhotoCount Photos taken since the last transfer, offered by "Send photos".
 * @property mirroring Notification mirroring summary, or `null` when mirroring is off.
 * @property transfers Most recent transfers, newest first.
 * @property now Reference time for relative times ("2 min ago"); injected so screenshots are stable.
 */
data class HomeUiState(
    val pc: PcCardState? = null,
    val phoneClipboardPreview: String? = null,
    val pcClipboardPreview: String? = null,
    val recentPhotoCount: Int = 0,
    val mirroring: MirroringSummary? = null,
    val transfers: List<TransferItem> = emptyList(),
    val now: Instant = Instant.EPOCH,
)

/**
 * The PC card at the top of the home screen.
 *
 * @property osLabel Operating system as reported in `Hello`, e.g. "Windows 11"; `null` until known.
 */
data class PcCardState(
    val name: String,
    val osLabel: String?,
    val connected: Boolean,
    val bluetooth: Boolean,
    val wifi: Boolean,
)

/** "Notifications go to your PC" row. */
data class MirroringSummary(
    val appCount: Int,
    val lastSender: String?,
    val lastAt: Instant?,
)

/** One row in "Recent transfers". [folder] is the destination folder on the PC for outgoing files. */
data class TransferItem(
    val id: String,
    val fileName: String,
    val incoming: Boolean,
    val at: Instant,
    val folder: String? = null,
)
