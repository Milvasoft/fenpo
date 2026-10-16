plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.home"
}

dependencies {
    implementation(project(":core:connection"))
}
