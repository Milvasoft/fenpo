package com.fenpo.android.core.data.di

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.preferencesDataStoreFile
import com.fenpo.android.core.data.device.AndroidLocalDeviceRepository
import com.fenpo.android.core.data.device.LocalDeviceRepository
import com.fenpo.android.core.data.hotspot.DataStoreHotspotRepository
import com.fenpo.android.core.data.hotspot.HotspotRepository
import com.fenpo.android.core.data.notifications.DataStoreNotificationAppsRepository
import com.fenpo.android.core.data.notifications.NotificationAppsRepository
import com.fenpo.android.core.data.pc.DataStorePairedPcRepository
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.data.permissions.PermissionChecker
import com.fenpo.android.core.data.permissions.SystemPermissionChecker
import com.fenpo.android.core.data.settings.AppCompatLanguageController
import com.fenpo.android.core.data.settings.DataStoreSettingsRepository
import com.fenpo.android.core.data.settings.LanguageController
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.transfer.DataStoreTransferHistory
import com.fenpo.android.core.data.transfer.TransferHistory
import dagger.Binds
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
internal abstract class DataModule {
    @Binds
    abstract fun settingsRepository(impl: DataStoreSettingsRepository): SettingsRepository

    @Binds
    abstract fun pairedPcRepository(impl: DataStorePairedPcRepository): PairedPcRepository

    @Binds
    abstract fun localDeviceRepository(impl: AndroidLocalDeviceRepository): LocalDeviceRepository

    @Binds
    abstract fun languageController(impl: AppCompatLanguageController): LanguageController

    @Binds
    abstract fun permissionChecker(impl: SystemPermissionChecker): PermissionChecker

    @Binds
    abstract fun transferHistory(impl: DataStoreTransferHistory): TransferHistory

    @Binds
    abstract fun hotspotRepository(impl: DataStoreHotspotRepository): HotspotRepository

    @Binds
    abstract fun notificationApps(impl: DataStoreNotificationAppsRepository): NotificationAppsRepository

    companion object {
        @Provides
        @Singleton
        fun preferencesDataStore(
            @ApplicationContext context: Context,
        ): DataStore<Preferences> =
            PreferenceDataStoreFactory.create { context.preferencesDataStoreFile("fenpo_settings") }
    }
}
