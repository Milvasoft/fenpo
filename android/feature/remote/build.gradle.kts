plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.remote"
}

dependencies {
    implementation(project(":core:connection"))
}
