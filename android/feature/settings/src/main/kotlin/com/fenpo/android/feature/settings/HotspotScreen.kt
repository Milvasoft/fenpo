package com.fenpo.android.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.navigation3.runtime.NavKey
import com.fenpo.android.core.data.hotspot.HotspotDetails
import com.fenpo.android.core.data.hotspot.HotspotRepository
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import javax.inject.Inject

/** Navigation key of Settings > Hotspot. */
@Serializable
data object HotspotKey : NavKey

/** What Settings > Hotspot shows: the saved details, `null` while loading. */
data class HotspotUiState(
    val details: HotspotDetails?,
)

@HiltViewModel
class HotspotViewModel
    @Inject
    constructor(
        private val repository: HotspotRepository,
    ) : ViewModel() {
        val state: StateFlow<HotspotUiState?> =
            repository.details
                .map { HotspotUiState(it) }
                .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

        fun save(
            ssid: String,
            passphrase: String,
        ) {
            viewModelScope.launch { repository.save(HotspotDetails(ssid.trim(), passphrase)) }
        }

        fun clear() {
            viewModelScope.launch { repository.clear() }
        }
    }

/** Settings > Hotspot with its view model. */
@Composable
fun HotspotRoute(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: HotspotViewModel = hiltViewModel(),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    HotspotScreen(
        state = state,
        onBack = onBack,
        onSave = { ssid, passphrase ->
            viewModel.save(ssid, passphrase)
            onBack()
        },
        onClear = viewModel::clear,
        modifier = modifier,
    )
}

/**
 * The phone's hotspot name and password for "Connect to hotspot" on the PC (hotspot.proto). The
 * design does not draw it; it uses the app's sub-screen bar, text fields and buttons. Android lets
 * no app read them, so the user types them once; they are kept encrypted on the phone.
 */
@Composable
fun HotspotScreen(
    state: HotspotUiState?,
    onBack: () -> Unit,
    onSave: (String, String) -> Unit,
    onClear: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var ssid by rememberSaveable { mutableStateOf("") }
    var passphrase by rememberSaveable { mutableStateOf("") }
    var filled by rememberSaveable { mutableStateOf(false) }
    LaunchedEffect(state) {
        if (state != null && !filled) {
            ssid = state.details?.ssid.orEmpty()
            passphrase = state.details?.passphrase.orEmpty()
            filled = true
        }
    }
    val valid = ssid.isNotBlank() && passphrase.length >= MIN_PASSPHRASE
    Column(modifier = modifier.fillMaxSize().imePadding().verticalScroll(rememberScrollState())) {
        FenpoSubTopBar(title = stringResource(R.string.hotspot_title), onBack = onBack)
        Column(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            OutlinedTextField(
                value = ssid,
                onValueChange = { ssid = it },
                label = { Text(stringResource(R.string.hotspot_name)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = passphrase,
                onValueChange = { passphrase = it },
                label = { Text(stringResource(R.string.hotspot_password)) },
                supportingText = { Text(stringResource(R.string.hotspot_password_hint)) },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                modifier = Modifier.fillMaxWidth(),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { onSave(ssid, passphrase) }, enabled = valid) {
                    Text(stringResource(R.string.hotspot_save))
                }
                if (state?.details != null) {
                    OutlinedButton(
                        onClick = {
                            ssid = ""
                            passphrase = ""
                            onClear()
                        },
                    ) { Text(stringResource(R.string.hotspot_remove)) }
                }
            }
            NoteBox(
                text = stringResource(R.string.hotspot_note),
                icon = FenpoIcons.Info,
                emphasized = false,
            )
        }
    }
}

/** WPA2 and WPA3 personal passwords are 8 to 63 characters. */
private const val MIN_PASSPHRASE = 8
