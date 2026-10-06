package com.natsx.controller

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidManifestLifecycleContractTest {
    @Test
    fun gameplayActivityKeepsLandscapeAndHandlesRotationConfiguration() {
        val manifest =
            File(
                "src/main/AndroidManifest.xml",
            ).readText()

        assertTrue(
            manifest.contains(
                "android:screenOrientation=\"sensorLandscape\"",
            ),
        )
        assertTrue(
            manifest.contains(
                "android:configChanges=\"keyboardHidden|orientation|screenSize\"",
            ),
        )
    }

    @Test
    fun stickyServiceRestartReentersConnectionBootstrap() {
        val serviceSource =
            File(
                "src/main/java/com/natsx/controller/service/ControllerService.kt",
            ).readText()

        assertTrue(
            serviceSource.contains(
                "override fun onStartCommand",
            ),
        )
        assertTrue(
            serviceSource.contains(
                "ensureConnectionBootstrap()\n        return START_STICKY",
            ),
        )
    }

    @Test
    fun controllerRuntimeRemainsAConnectedDeviceForegroundService() {
        val manifest =
            File(
                "src/main/AndroidManifest.xml",
            ).readText()

        assertTrue(
            manifest.contains(
                "android:name=\".service.ControllerService\"",
            ),
        )
        assertTrue(
            manifest.contains(
                "android:foregroundServiceType=\"connectedDevice\"",
            ),
        )
    }
}
