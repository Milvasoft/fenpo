plugins {
    alias(libs.plugins.fenpo.android.library)
    alias(libs.plugins.fenpo.hilt)
}

android {
    namespace = "com.fenpo.android.core.data"
}

dependencies {
    api(libs.kotlinx.coroutines.core)
    api(project(":core:security"))
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.core.ktx)

    testImplementation(libs.turbine)
}
