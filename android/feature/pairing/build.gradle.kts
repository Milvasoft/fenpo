plugins {
    alias(libs.plugins.fenpo.android.feature)
}

android {
    namespace = "com.fenpo.android.feature.pairing"
}

dependencies {
    implementation(project(":core:pairing"))
    implementation(libs.androidx.camera.camera2)
    implementation(libs.androidx.camera.lifecycle)
    implementation(libs.androidx.camera.view)
    implementation(libs.zxing.cpp)
}
