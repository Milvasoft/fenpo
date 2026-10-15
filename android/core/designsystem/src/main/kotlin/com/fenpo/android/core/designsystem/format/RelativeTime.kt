package com.fenpo.android.core.designsystem.format

import androidx.compose.runtime.Composable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import com.fenpo.android.core.designsystem.R
import java.time.Duration
import java.time.Instant

/**
 * Short relative time ("now", "2 min ago", "3 h ago", "Yesterday") in the app language. Uses
 * Fenpo's own strings instead of the system formatter so it follows the per-app language.
 */
@Composable
@ReadOnlyComposable
fun relativeTime(
    at: Instant,
    now: Instant,
): String {
    val elapsed = Duration.between(at, now)
    val minutes = elapsed.toMinutes()
    val hours = elapsed.toHours()
    val days = elapsed.toDays()
    return when {
        minutes < 1 -> stringResource(R.string.time_now)
        hours < 1 -> pluralStringResource(R.plurals.time_minutes_ago, minutes.toInt(), minutes.toInt())
        days < 1 -> pluralStringResource(R.plurals.time_hours_ago, hours.toInt(), hours.toInt())
        days < 2 -> stringResource(R.string.time_yesterday)
        else -> pluralStringResource(R.plurals.time_days_ago, days.toInt(), days.toInt())
    }
}
