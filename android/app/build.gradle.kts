plugins {
    id("com.android.application")
}

android {
    namespace = "com.natsx.controller"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.natsx.controller"
        minSdk = 26
        targetSdk = 36
        versionCode = 2
        versionName = "0.1.1-dev"

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
