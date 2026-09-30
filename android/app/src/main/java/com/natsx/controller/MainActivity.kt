package com.natsx.controller

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.view.View
import android.view.WindowManager
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.transport.bluetooth.BluetoothPermissionGate
import com.natsx.controller.feature.controller.ControllerSurfaceView
import com.natsx.controller.service.ControllerService

class MainActivity : Activity() {
    private lateinit var stateStore: GamepadStateStore
    private lateinit var controllerView: ControllerSurfaceView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        window.addFlags(
            WindowManager.LayoutParams.FLAG_FULLSCREEN or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
        )

        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility =
            View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE

        stateStore = (application as NatsxControllerApplication).gamepadStateStore
        controllerView = ControllerSurfaceView(this, stateStore)
        setContentView(controllerView)

        requestBluetoothPermissionsIfNeeded()

        startForegroundService(Intent(this, ControllerService::class.java))
    }

    private fun requestBluetoothPermissionsIfNeeded() {
        val permissionGate = BluetoothPermissionGate(this)

        if (!permissionGate.isBluetoothSupported()) {
            return
        }

        val missingPermissions =
            permissionGate.missingRuntimePermissions()

        if (missingPermissions.isNotEmpty()) {
            requestPermissions(
                missingPermissions.toTypedArray(),
                REQUEST_BLUETOOTH_PERMISSIONS,
            )
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)

        if (hasFocus) {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility =
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                    View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        } else if (::controllerView.isInitialized) {
            controllerView.releaseAllInputs()
        }
    }

    override fun onStop() {
        if (::controllerView.isInitialized) {
            controllerView.releaseAllInputs()
        }

        super.onStop()
    }

    private companion object {
        const val REQUEST_BLUETOOTH_PERMISSIONS = 2001
    }
}
