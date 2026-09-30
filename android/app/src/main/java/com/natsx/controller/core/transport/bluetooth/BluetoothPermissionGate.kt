package com.natsx.controller.core.transport.bluetooth

import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build

class BluetoothPermissionGate(
    private val context: Context,
) {
    fun missingRuntimePermissions(): List<String> {
        return BluetoothPermissionPolicy
            .requiredRuntimePermissions(Build.VERSION.SDK_INT)
            .filter { permission ->
                context.checkSelfPermission(permission) !=
                    PackageManager.PERMISSION_GRANTED
            }
    }

    fun hasRequiredRuntimePermissions(): Boolean {
        return missingRuntimePermissions().isEmpty()
    }

    fun isBluetoothSupported(): Boolean {
        return context.packageManager.hasSystemFeature(
            PackageManager.FEATURE_BLUETOOTH,
        )
    }

    fun isBluetoothEnabled(): Boolean {
        if (!isBluetoothSupported() ||
            !hasRequiredRuntimePermissions()
        ) {
            return false
        }

        val manager =
            context.getSystemService(BluetoothManager::class.java)

        return manager?.adapter?.isEnabled == true
    }
}
