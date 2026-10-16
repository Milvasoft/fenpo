plugins {
    alias(libs.plugins.fenpo.android.library)
    alias(libs.plugins.fenpo.hilt)
}

android {
    namespace = "com.fenpo.android.core.media"
}

dependencies {
    api(project(":core:connection"))
}
