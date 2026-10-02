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

    buildTypes {
        release {
            isMinifyEnabled = false
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
