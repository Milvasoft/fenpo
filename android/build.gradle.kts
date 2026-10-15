plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.library) apply false
    alias(libs.plugins.kotlin.jvm) apply false
    alias(libs.plugins.kotlin.compose) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.ksp) apply false
    alias(libs.plugins.hilt) apply false
    alias(libs.plugins.wire) apply false
    alias(libs.plugins.roborazzi) apply false
    alias(libs.plugins.spotless)
}

spotless {
    val ktlintVersion = libs.versions.ktlint.get()
    val composeRules = "io.nlopez.compose.rules:ktlint:${libs.versions.composeRules.get()}"
    // Source folders only: walking build/ races with tasks that write there.
    kotlin {
        target("app/src/**/*.kt", "core/*/src/**/*.kt", "feature/*/src/**/*.kt", "build-logic/convention/src/**/*.kt")
        ktlint(ktlintVersion).customRuleSets(listOf(composeRules))
    }
    kotlinGradle {
        target(
            "*.gradle.kts",
            "app/*.gradle.kts",
            "core/*/*.gradle.kts",
            "feature/*/*.gradle.kts",
            "build-logic/*.gradle.kts",
            "build-logic/convention/*.gradle.kts",
        )
        ktlint(ktlintVersion)
    }
}
