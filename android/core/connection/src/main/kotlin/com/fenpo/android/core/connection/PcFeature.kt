package com.fenpo.android.core.connection

import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.Multibinds

/**
 * A feature module's part of the connection: it handles its PC messages while the connection
 * service runs. Feature modules contribute one with `@Binds @IntoSet`; the connection service
 * starts them all, so this module needs no dependency on them.
 */
interface PcFeature {
    /** Runs until cancelled, usually collecting [PcMessenger.incoming]. */
    suspend fun run()
}

@Module
@InstallIn(SingletonComponent::class)
internal abstract class PcFeatureModule {
    @Multibinds
    abstract fun features(): Set<PcFeature>
}
