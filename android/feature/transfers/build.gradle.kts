plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.transfers"
}

dependencies {
    implementation(project(":core:connection"))
}
