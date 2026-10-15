plugins {
    alias(libs.plugins.fenpo.jvm.library)
}

dependencies {
    api(project(":core:protocol"))
    api(project(":core:transport"))

    testImplementation(testFixtures(project(":core:transport")))
}
