package com.fenpo.android.feature.pairing

import androidx.lifecycle.ViewModel
import com.fenpo.android.core.data.permissions.AppPermission
import com.fenpo.android.core.data.permissions.PermissionChecker
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject

/** One row of the Permissions screen. */
data class PermissionItem(
    val permission: AppPermission,
    val granted: Boolean,
)

/** Keeps the permission list current; [refresh] runs whenever the screen resumes. */
@HiltViewModel
class PermissionsViewModel
    @Inject
    constructor(
        private val checker: PermissionChecker,
    ) : ViewModel() {
        private val _items = MutableStateFlow(read())
        val items: StateFlow<List<PermissionItem>> = _items.asStateFlow()

        /** Re-reads every permission, e.g. after the user comes back from system Settings. */
        fun refresh() {
            _items.value = read()
        }

        private fun read() = AppPermission.shown.map { PermissionItem(it, checker.isGranted(it)) }
    }
