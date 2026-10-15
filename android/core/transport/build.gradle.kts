plugins {
    alias(libs.plugins.fenpo.jvm.library)
    `java-test-fixtures`
}

dependencies {
    api(project(":core:protocol"))
    api(libs.kotlinx.coroutines.core)

    // In-process TLS server and test certificates, shared with :core:pairing tests.
    testFixturesApi(libs.kotlinx.coroutines.core)
}
