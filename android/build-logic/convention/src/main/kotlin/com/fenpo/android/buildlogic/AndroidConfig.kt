package com.fenpo.android.buildlogic

import com.android.build.api.dsl.CommonExtension
import org.gradle.api.JavaVersion
import org.gradle.api.Project
import org.gradle.api.artifacts.VersionCatalog
import org.gradle.api.artifacts.VersionCatalogsExtension
import org.gradle.kotlin.dsl.getByType

/** Android API levels shared by every module (docs/ANDROID.md §12). */
internal object FenpoSdk {
    const val COMPILE = 37
    const val TARGET = 37
    const val MIN = 31
}

internal val Project.libs: VersionCatalog
    get() = extensions.getByType<VersionCatalogsExtension>().named("libs")

internal fun VersionCatalog.library(alias: String) = findLibrary(alias).get()

/**
 * Settings every Android module shares: SDK levels, Java 17 bytecode and unit tests that can
 * read Android resources (Robolectric and Roborazzi need them).
 */
internal fun Project.configureAndroidCommon(extension: CommonExtension) {
    extension.apply {
        compileSdk = FenpoSdk.COMPILE
        defaultConfig.minSdk = FenpoSdk.MIN

        compileOptions.sourceCompatibility = JavaVersion.VERSION_17
        compileOptions.targetCompatibility = JavaVersion.VERSION_17

        testOptions.unitTests.isIncludeAndroidResources = true
        // Robolectric reaches into FileDescriptor internals, which JDK 21 hides by default.
        testOptions.unitTests.all {
            it.jvmArgs(
                "--add-exports=java.base/jdk.internal.access=ALL-UNNAMED",
                "--add-opens=java.base/java.io=ALL-UNNAMED",
            )
        }

        lint.abortOnError = true
        lint.warningsAsErrors = false
        lint.checkDependencies = true
        // Versions are chosen on purpose in libs.versions.toml (docs/ANDROID.md §14), not by lint.
        lint.disable += setOf("AndroidGradlePluginVersion", "GradleDependency", "NewerVersionAvailable")
    }
}
