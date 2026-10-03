import java.security.KeyStore
import java.security.PrivateKey
import java.security.cert.X509Certificate
import javax.naming.ldap.LdapName

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

        testInstrumentationRunner = "com.natsx.controller.SkinVerificationInstrumentation"
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

        val password = checkNotNull(releaseStorePassword).toCharArray()
        val keyPassword = checkNotNull(releaseKeyPassword).toCharArray()
        try {
            val store = KeyStore.getInstance(storePath, password)
            val alias = checkNotNull(releaseKeyAlias)
            check(store.isKeyEntry(alias)) { "Android release alias must contain a private key." }
            check(store.getKey(alias, keyPassword) is PrivateKey) {
                "Android release signing key is unavailable."
            }
            val certificate = store.getCertificate(alias) as? X509Certificate
                ?: error("Android release signing certificate is unavailable.")
            certificate.checkValidity()
            val debugSubject = LdapName(certificate.subjectX500Principal.name)
                .rdns.any { it.type.equals("CN", ignoreCase = true) &&
                    it.value.toString().equals("Android Debug", ignoreCase = true) }
            check(!alias.equals("androiddebugkey", ignoreCase = true) && !debugSubject) {
                "Android Debug signing identities cannot be used for production release."
            }
        } finally {
            password.fill('\u0000')
            keyPassword.fill('\u0000')
        }
    }
}

// Production assembly must never silently fall back to an unsigned/debug APK.
tasks.matching { it.name == "preReleaseBuild" }.configureEach {
    dependsOn("verifyReleaseSigning")
}
