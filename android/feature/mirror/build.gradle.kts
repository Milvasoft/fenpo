plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.mirror"
}

dependencies {
    implementation(project(":core:media"))
}
