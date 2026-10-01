package com.natsx.controller.core.transport.bluetooth

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BluetoothBondingManagerTest {
    @Test
    fun alreadyBondedDoesNotRequestNewBond() {
        val device =
            FakeBondDevice(
                BluetoothBondStatus.BONDED,
            )

        val manager =
            BluetoothBondingManager(
                nowMillis = { 0L },
                sleeper = {},
            )

        assertEquals(
            BluetoothPairingResult.ALREADY_BONDED,
            manager.ensureBonded(device),
        )
        assertFalse(device.requested)
    }

    @Test
    fun osBondRequestCompletesWhenDeviceBecomesBonded() {
        var now = 0L

        val device =
            FakeBondDevice(
                BluetoothBondStatus.NOT_BONDED,
            )

        val manager =
            BluetoothBondingManager(
                nowMillis = { now },
                sleeper = { delay ->
                    now += delay

                    if (now >= 300L) {
                        device.currentStatus =
                            BluetoothBondStatus.BONDED
                    }
                },
                pollIntervalMillis = 100,
            )

        assertEquals(
            BluetoothPairingResult.BONDED,
            manager.ensureBonded(
                device,
                timeoutMillis = 1_000,
            ),
        )
        assertTrue(device.requested)
    }

    @Test
    fun rejectedBondRequestFailsImmediately() {
        val device =
            FakeBondDevice(
                BluetoothBondStatus.NOT_BONDED,
                requestAccepted = false,
            )

        val manager =
            BluetoothBondingManager(
                nowMillis = { 0L },
                sleeper = {},
            )

        assertEquals(
            BluetoothPairingResult.REQUEST_REJECTED,
            manager.ensureBonded(device),
        )
        assertTrue(device.requested)
    }

    @Test
    fun bondingTimesOutWithoutPlatformConfirmation() {
        var now = 0L

        val device =
            FakeBondDevice(
                BluetoothBondStatus.BONDING,
            )

        val manager =
            BluetoothBondingManager(
                nowMillis = { now },
                sleeper = { delay ->
                    now += delay
                },
                pollIntervalMillis = 100,
            )

        assertEquals(
            BluetoothPairingResult.TIMEOUT,
            manager.ensureBonded(
                device,
                timeoutMillis = 1_000,
            ),
        )
        assertFalse(device.requested)
    }

    private class FakeBondDevice(
        initialStatus: BluetoothBondStatus,
        private val requestAccepted: Boolean = true,
    ) : BluetoothBondDevice {
        var currentStatus:
            BluetoothBondStatus = initialStatus

        var requested: Boolean = false
            private set

        override val status: BluetoothBondStatus
            get() = currentStatus

        override fun requestBond(): Boolean {
            requested = true

            if (requestAccepted) {
                currentStatus =
                    BluetoothBondStatus.BONDING
            }

            return requestAccepted
        }
    }
}
