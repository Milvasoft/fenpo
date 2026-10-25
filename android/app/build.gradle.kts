plugins {
    alias(libs.plugins.fenpo.android.application)
    alias(libs.plugins.fenpo.android.compose)
    alias(libs.plugins.fenpo.hilt)
    alias(libs.plugins.kotlin.serialization)
}

// The Google Play upload key (docs/android/PLAY-LISTING.md). Buğra keeps the keystore and its
// passwords outside the repository: in ~/.gradle/gradle.properties or in environment variables.
// Without them the release bundle is built unsigned. Play App Signing signs what users install.
fun secret(name: String): String? = providers.gradleProperty(name).orNull ?: providers.environmentVariable(name).orNull

val uploadStore = secret("FENPO_UPLOAD_STORE_FILE")

android {
    namespace = "com.fenpo.android"

    defaultConfig {
        applicationId = "com.fenpo.android"
        // Every upload to Play needs a higher versionCode.
        versionCode = 4
        versionName = "1.0.3"
    }

    signingConfigs {
        if (uploadStore != null) {
            create("upload") {
                storeFile = file(uploadStore)
                storePassword = secret("FENPO_UPLOAD_STORE_PASSWORD")
                keyAlias = secret("FENPO_UPLOAD_KEY_ALIAS")
                keyPassword = secret("FENPO_UPLOAD_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            if (uploadStore != null) signingConfig = signingConfigs.getByName("upload")
        }
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
