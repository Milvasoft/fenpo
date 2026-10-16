plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.camera"
}

dependencies {
    implementation(project(":core:media"))
}
