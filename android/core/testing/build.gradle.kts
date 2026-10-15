plugins {
    alias(libs.plugins.fenpo.android.library)
    alias(libs.plugins.fenpo.android.compose)
}

android {
    namespace = "com.fenpo.android.core.testing"
}

dependencies {
    api(project(":core:designsystem"))
    api(libs.robolectric)
    api(libs.roborazzi)
    api(libs.roborazzi.compose)
    api(libs.junit4)
    api(libs.androidx.test.ext.junit)
}
