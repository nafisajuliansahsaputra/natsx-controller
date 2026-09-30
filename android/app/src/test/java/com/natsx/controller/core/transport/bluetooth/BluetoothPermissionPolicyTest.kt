package com.natsx.controller.core.transport.bluetooth

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BluetoothPermissionPolicyTest {
    @Test
    fun preAndroid12RequiresNoRuntimeBluetoothPermissions() {
        assertTrue(
            BluetoothPermissionPolicy
                .requiredRuntimePermissions(30)
                .isEmpty(),
        )
    }

    @Test
    fun android12AndLaterRequireConnectAndScan() {
        assertEquals(
            listOf(
                BluetoothPermissionPolicy
                    .BLUETOOTH_CONNECT_PERMISSION,
                BluetoothPermissionPolicy
                    .BLUETOOTH_SCAN_PERMISSION,
            ),
            BluetoothPermissionPolicy
                .requiredRuntimePermissions(31),
        )

        assertEquals(
            BluetoothPermissionPolicy
                .requiredRuntimePermissions(31),
            BluetoothPermissionPolicy
                .requiredRuntimePermissions(36),
        )
    }
}
