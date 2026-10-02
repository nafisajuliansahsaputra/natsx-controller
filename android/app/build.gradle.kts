plugins {
    id("com.android.application")
}

val natsxVersionName =
    rootProject
        .file("../VERSION")
        .readText()
        .trim()

val natsxVersionCode =
    rootProject
        .file("../VERSION_CODE")
        .readText()
        .trim()
        .toInt()

fun releaseValue(
    name: String,
): String? =
    providers
        .gradleProperty(name)
        .orElse(
            providers
                .environmentVariable(name),
        )
        .orNull
        ?.takeIf {
            it.isNotBlank()
        }

val releaseStoreFile =
    releaseValue(
        "NATSX_RELEASE_STORE_FILE",
    )
val releaseStorePassword =
    releaseValue(
        "NATSX_RELEASE_STORE_PASSWORD",
    )
val releaseKeyAlias =
    releaseValue(
        "NATSX_RELEASE_KEY_ALIAS",
    )
val releaseKeyPassword =
    releaseValue(
        "NATSX_RELEASE_KEY_PASSWORD",
    )

val hasReleaseSigning =
    listOf(
        releaseStoreFile,
        releaseStorePassword,
        releaseKeyAlias,
        releaseKeyPassword,
    ).all {
        it != null
    }

android {
    namespace = "com.natsx.controller"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.natsx.controller"
        minSdk = 26
        targetSdk = 36
        versionCode = natsxVersionCode
        versionName = natsxVersionName

        testInstrumentationRunner = "android.app.Instrumentation"
    }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile =
                    file(
                        checkNotNull(
                            releaseStoreFile,
                        ),
                    )
                storePassword =
                    checkNotNull(
                        releaseStorePassword,
                    )
                keyAlias =
                    checkNotNull(
                        releaseKeyAlias,
                    )
                keyPassword =
                    checkNotNull(
                        releaseKeyPassword,
                    )
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false

            if (hasReleaseSigning) {
                signingConfig =
                    signingConfigs
                        .getByName(
                            "release",
                        )
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests.all {
            it.useJUnit()
        }
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}


tasks.register("verifyReleaseSigning") {
    doLast {
        check(hasReleaseSigning) {
            "Android release signing is not configured. Provide NATSX_RELEASE_STORE_FILE, NATSX_RELEASE_STORE_PASSWORD, NATSX_RELEASE_KEY_ALIAS, and NATSX_RELEASE_KEY_PASSWORD."
        }

        val storePath =
            file(
                checkNotNull(
                    releaseStoreFile,
                ),
            )

        check(storePath.isFile) {
            "Android release keystore does not exist: " +
                storePath.absolutePath
        }
    }
}
