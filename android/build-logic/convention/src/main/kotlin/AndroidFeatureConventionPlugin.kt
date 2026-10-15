import com.fenpo.android.buildlogic.library
import com.fenpo.android.buildlogic.libs
import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.kotlin.dsl.dependencies

/**
 * Configures a `feature/` module: Android library with Compose, Hilt, Navigation 3 keys and
 * Roborazzi screenshot tests in both themes and both languages.
 */
class AndroidFeatureConventionPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        with(target) {
            pluginManager.apply("fenpo.android.library")
            pluginManager.apply("fenpo.android.compose")
            pluginManager.apply("fenpo.hilt")
            pluginManager.apply("org.jetbrains.kotlin.plugin.serialization")
            pluginManager.apply("io.github.takahirom.roborazzi")

            dependencies {
                add("implementation", project(":core:designsystem"))
                add("implementation", project(":core:data"))
                add("implementation", libs.library("androidx-activity-compose"))
                add("implementation", libs.library("androidx-lifecycle-runtime-compose"))
                add("implementation", libs.library("androidx-lifecycle-viewmodel-compose"))
                add("implementation", libs.library("androidx-hilt-lifecycle-viewmodel-compose"))
                add("implementation", libs.library("androidx-navigation3-runtime"))
                add("implementation", libs.library("kotlinx-serialization-core"))

                add("testImplementation", project(":core:testing"))
                add("testImplementation", libs.library("robolectric"))
                add("testImplementation", libs.library("roborazzi"))
                add("testImplementation", libs.library("roborazzi-compose"))
                add("testImplementation", libs.library("androidx-test-ext-junit"))
                add("testImplementation", libs.library("androidx-compose-ui-test-junit4"))
                add("debugImplementation", libs.library("androidx-compose-ui-test-manifest"))
            }
        }
    }
}
