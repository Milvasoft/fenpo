plugins {
    alias(libs.plugins.fenpo.jvm.library)
}

dependencies {
    api(project(":core:protocol"))
}
