package com.natsx.controller

import android.app.Application
import com.natsx.controller.core.gamepad.GamepadStateStore

class NatsxControllerApp : Application() {
    val gamepadStateStore: GamepadStateStore by lazy {
        GamepadStateStore()
    }
}
