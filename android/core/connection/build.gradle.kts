plugins {
    alias(libs.plugins.fenpo.android.library)
    alias(libs.plugins.fenpo.hilt)
}

android {
    namespace = "com.fenpo.android.core.connection"
}

dependencies {
    api(project(":core:ble"))
    api(project(":core:data"))
    implementation(project(":core:designsystem"))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity)
    implementation(libs.androidx.lifecycle.runtime.ktx)

    testImplementation(libs.robolectric)
    testImplementation(testFixtures(project(":core:transport")))
    testImplementation(libs.androidx.test.ext.junit)
}
