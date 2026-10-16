plugins {
    alias(libs.plugins.fenpo.android.library)
}

android {
    namespace = "com.fenpo.android.core.ble"
}

dependencies {
    api(project(":core:transport"))
    implementation(libs.androidx.core.ktx)
}
