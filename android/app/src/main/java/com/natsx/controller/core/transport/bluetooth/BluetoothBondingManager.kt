package com.natsx.controller.core.transport.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.os.SystemClock

enum class BluetoothBondStatus {
    NOT_BONDED,
    BONDING,
    BONDED,
}

interface BluetoothBondDevice {
    val status: BluetoothBondStatus

    fun requestBond(): Boolean
}

/**
 * Thin adapter around BluetoothDevice.
 *
 * Runtime callers must pass through BluetoothPermissionGate before creating or
 * using this adapter. Permission can still be revoked asynchronously, so the
 * higher-level pairing flow treats SecurityException as a connection failure.
 */
@SuppressLint("MissingPermission")
class AndroidBluetoothBondDevice(
    private val device: BluetoothDevice,
) : BluetoothBondDevice {
    override val status: BluetoothBondStatus
        get() =
            when (device.bondState) {
                BluetoothDevice.BOND_BONDED ->
                    BluetoothBondStatus.BONDED
                BluetoothDevice.BOND_BONDING ->
                    BluetoothBondStatus.BONDING
                else ->
                    BluetoothBondStatus.NOT_BONDED
            }

    override fun requestBond(): Boolean =
        device.createBond()
}

enum class BluetoothPairingResult {
    ALREADY_BONDED,
    BONDED,
    REQUEST_REJECTED,
    TIMEOUT,
}

/**
 * Starts Android's normal OS Bluetooth bonding flow.
 *
 * createBond() intentionally leaves PIN / numeric confirmation to the platform
 * UI. This manager never invents or stores a Bluetooth PIN.
 *
 * The blocking wait must run off the Android main thread.
 */
class BluetoothBondingManager(
    private val nowMillis: () -> Long =
        SystemClock::elapsedRealtime,
    private val sleeper: (Long) -> Unit = {
        Thread.sleep(it)
    },
    private val pollIntervalMillis: Long =
        DEFAULT_POLL_INTERVAL_MILLIS,
) {
    init {
        require(pollIntervalMillis in 25..1_000)
    }

    fun ensureBonded(
        device: BluetoothBondDevice,
        timeoutMillis: Long =
            DEFAULT_PAIRING_TIMEOUT_MILLIS,
    ): BluetoothPairingResult {
        require(timeoutMillis in 1_000..120_000)

        if (device.status ==
            BluetoothBondStatus.BONDED
        ) {
            return BluetoothPairingResult.ALREADY_BONDED
        }

        if (
            device.status ==
                BluetoothBondStatus.NOT_BONDED &&
            !device.requestBond()
        ) {
            return BluetoothPairingResult.REQUEST_REJECTED
        }

        val deadline =
            nowMillis() + timeoutMillis

        while (nowMillis() < deadline) {
            if (device.status ==
                BluetoothBondStatus.BONDED
            ) {
                return BluetoothPairingResult.BONDED
            }

            sleeper(pollIntervalMillis)
        }

        return if (
            device.status ==
            BluetoothBondStatus.BONDED
        ) {
            BluetoothPairingResult.BONDED
        } else {
            BluetoothPairingResult.TIMEOUT
        }
    }

    companion object {
        const val DEFAULT_PAIRING_TIMEOUT_MILLIS =
            30_000L
        const val DEFAULT_POLL_INTERVAL_MILLIS =
            100L
    }
}
