package com.natsx.controller.core.gamepad

fun interface GamepadStateListener {
    fun onGamepadStateChanged(state: GamepadState)
}
