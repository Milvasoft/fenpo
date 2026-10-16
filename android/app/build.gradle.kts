plugins {
    alias(libs.plugins.fenpo.android.application)
    alias(libs.plugins.fenpo.android.compose)
    alias(libs.plugins.fenpo.hilt)
    alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "com.fenpo.android"

    defaultConfig {
        applicationId = "com.fenpo.android"
        versionCode = 1
        versionName = "0.1.0"
    }

    androidResources {
        // Builds the locale list for the system's per-app language setting from values-* folders.
        generateLocaleConfig = true
    }
}

dependencies {
    implementation(project(":core:designsystem"))
    implementation(project(":core:data"))
    implementation(project(":core:connection"))
    implementation(project(":feature:home"))
    implementation(project(":feature:pairing"))
    implementation(project(":feature:transfers"))
    implementation(project(":feature:settings"))
    implementation(project(":feature:camera"))
    implementation(project(":feature:mirror"))
    implementation(project(":feature:remote"))

    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.core.splashscreen)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.viewmodel.navigation3)
    implementation(libs.androidx.navigation3.runtime)
    implementation(libs.androidx.navigation3.ui)
    implementation(libs.androidx.hilt.lifecycle.viewmodel.compose)
    implementation(libs.kotlinx.serialization.core)
}
