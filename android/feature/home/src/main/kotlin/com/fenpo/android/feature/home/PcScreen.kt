package com.fenpo.android.feature.home

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar

/** PC tab with its view model. */
@Composable
fun PcRoute(
    onPair: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: HomeViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    PcScreen(pc = state.pc, onPair = onPair, modifier = modifier)
}

/**
 * PC tab. The design does not detail this screen yet; for now it shows the same PC card as the
 * home screen. Folders, battery and "Ring" come with the features that fill them.
 */
@Composable
fun PcScreen(
    pc: PcCardState?,
    onPair: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.fillMaxSize()) {
        FenpoRootTopBar(title = stringResource(R.string.pc_title))
        Column(modifier = Modifier.padding(horizontal = 16.dp)) {
            if (pc != null) PcCard(pc) else NotPairedCard(onPair = onPair)
        }
    }
}
