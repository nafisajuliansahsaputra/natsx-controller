package com.natsx.controller.core.transport.bluetooth

object BluetoothPermissionPolicy {
    const val ANDROID_12_API_LEVEL = 31

    const val BLUETOOTH_CONNECT_PERMISSION =
        "android.permission.BLUETOOTH_CONNECT"

    const val BLUETOOTH_SCAN_PERMISSION =
        "android.permission.BLUETOOTH_SCAN"

    fun requiredRuntimePermissions(apiLevel: Int): List<String> {
        return if (apiLevel >= ANDROID_12_API_LEVEL) {
            listOf(
                BLUETOOTH_CONNECT_PERMISSION,
                BLUETOOTH_SCAN_PERMISSION,
            )
        } else {
            emptyList()
        }
    }
}
