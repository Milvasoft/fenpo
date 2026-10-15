plugins {
    alias(libs.plugins.fenpo.android.library)
    alias(libs.plugins.fenpo.hilt)
}

android {
    namespace = "com.fenpo.android.core.security"

    defaultConfig {
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
}

dependencies {
    api(project(":core:transport"))

    // The Android Keystore only exists on a phone or emulator, so these run as instrumented tests.
    androidTestImplementation(testFixtures(project(":core:transport")))
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.kotlin.test)
    androidTestImplementation(libs.kotlinx.coroutines.test)
}

// Everything here needs the Android Keystore, so all tests are instrumented (src/androidTest).
tasks.withType<Test>().configureEach {
    failOnNoDiscoveredTests = false
}
