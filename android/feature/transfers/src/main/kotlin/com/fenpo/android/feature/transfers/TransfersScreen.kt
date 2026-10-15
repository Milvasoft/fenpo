package com.fenpo.android.feature.transfers

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar

/**
 * Transfers tab. The design does not detail this screen yet; until file transfer exists (phase D)
 * it shows the empty state.
 */
@Composable
fun TransfersScreen(modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxSize()) {
        FenpoRootTopBar(title = stringResource(R.string.transfers_title))
        FenpoCard(modifier = Modifier.padding(horizontal = 16.dp).fillMaxWidth()) {
            Column(modifier = Modifier.padding(20.dp)) {
                Text(stringResource(R.string.transfers_empty_title), style = MaterialTheme.typography.titleMedium)
                Text(
                    text = stringResource(R.string.transfers_empty_body),
                    modifier = Modifier.padding(top = 8.dp),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}
