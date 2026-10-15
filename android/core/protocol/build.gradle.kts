plugins {
    alias(libs.plugins.fenpo.jvm.library)
    alias(libs.plugins.wire)
}

// Types are generated from the shared contract in the repository's protocol/ folder; they are
// never written by hand (protocol/README.md).
wire {
    sourcePath {
        srcDir(rootDir.resolve("../protocol"))
    }
    kotlin {
        oneofMode = "sealed_class"
    }
}

dependencies {
    api(libs.wire.runtime)
}
