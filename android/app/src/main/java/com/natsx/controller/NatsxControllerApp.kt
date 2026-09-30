package com.natsx.controller

import android.app.Application
import android.bluetooth.BluetoothManager
import com.natsx.controller.core.connection.ControllerConnectionRuntime
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.haptics.AndroidControllerHaptics
import com.natsx.controller.haptics.ControllerHapticSettings
import com.natsx.controller.security.AndroidDeviceIdentityStore
import com.natsx.controller.security.AndroidTrustedReceiverStore

class NatsxControllerApp : Application() {
    val gamepadStateStore: GamepadStateStore by lazy {
        GamepadStateStore()
    }

    val deviceIdentityStore: AndroidDeviceIdentityStore by lazy {
        AndroidDeviceIdentityStore(this)
    }

    val trustedReceiverStore: AndroidTrustedReceiverStore by lazy {
        AndroidTrustedReceiverStore(this)
    }

    val hapticSettings: ControllerHapticSettings by lazy {
        ControllerHapticSettings(this)
    }

    val controllerHaptics: AndroidControllerHaptics by lazy {
        AndroidControllerHaptics(
            context = this,
            settings = hapticSettings,
        )
    }

    val connectionRuntime: ControllerConnectionRuntime by lazy {
        ControllerConnectionRuntime(
            androidDeviceId = deviceIdentityStore.getOrCreate(),
            stateStore = gamepadStateStore,
            trustedReceivers = trustedReceiverStore,
            haptics = controllerHaptics,
            bluetoothAdapter =
                getSystemService(
                    BluetoothManager::class.java,
                )?.adapter,
        )
    }
}
