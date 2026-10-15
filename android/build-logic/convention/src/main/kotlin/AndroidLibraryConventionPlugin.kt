import com.android.build.api.dsl.LibraryExtension
import com.fenpo.android.buildlogic.configureAndroidCommon
import com.fenpo.android.buildlogic.library
import com.fenpo.android.buildlogic.libs
import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.kotlin.dsl.configure
import org.gradle.kotlin.dsl.dependencies

/** Configures Android library modules under `core/` and `feature/`. */
class AndroidLibraryConventionPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        with(target) {
            pluginManager.apply("com.android.library")

            extensions.configure<LibraryExtension> {
                configureAndroidCommon(this)
            }

            dependencies {
                add("testImplementation", libs.library("junit4"))
                add("testImplementation", libs.library("kotlin-test"))
                add("testImplementation", libs.library("kotlinx-coroutines-test"))
            }
        }
    }
}
