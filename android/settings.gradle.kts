pluginManagement {
    includeBuild("build-logic")
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "fenpo"

include(":app")
include(":core:protocol")
include(":core:transport")
include(":core:pairing")
include(":core:security")
include(":core:ble")
include(":core:connection")
include(":core:data")
include(":core:designsystem")
include(":core:testing")
include(":feature:home")
include(":feature:pairing")
include(":feature:transfers")
include(":feature:settings")
