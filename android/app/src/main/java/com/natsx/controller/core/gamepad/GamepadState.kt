package com.natsx.controller.core.gamepad

data class GamepadState(
    val buttons: Int = GamepadButtons.NONE,
    val dpad: Int = DpadState.NEUTRAL,
    val leftX: Int = 0,
    val leftY: Int = 0,
    val rightX: Int = 0,
    val rightY: Int = 0,
    val leftTrigger: Int = 0,
    val rightTrigger: Int = 0,
) {
    init {
        require(leftX in STICK_MIN..STICK_MAX)
        require(leftY in STICK_MIN..STICK_MAX)
        require(rightX in STICK_MIN..STICK_MAX)
        require(rightY in STICK_MIN..STICK_MAX)
        require(leftTrigger in TRIGGER_MIN..TRIGGER_MAX)
        require(rightTrigger in TRIGGER_MIN..TRIGGER_MAX)
    }

    companion object {
        const val STICK_MIN = -32768
        const val STICK_MAX = 32767
        const val TRIGGER_MIN = 0
        const val TRIGGER_MAX = 255

        val Neutral = GamepadState()
    }
}

object GamepadButtons {
    const val NONE = 0
    const val A = 1 shl 0
    const val B = 1 shl 1
    const val X = 1 shl 2
    const val Y = 1 shl 3
    const val LEFT_SHOULDER = 1 shl 4
    const val RIGHT_SHOULDER = 1 shl 5
    const val LEFT_STICK = 1 shl 6
    const val RIGHT_STICK = 1 shl 7
    const val BACK = 1 shl 8
    const val START = 1 shl 9
    const val GUIDE = 1 shl 10
}

object DpadState {
    const val NEUTRAL = 0
    const val UP = 1 shl 0
    const val DOWN = 1 shl 1
    const val LEFT = 1 shl 2
    const val RIGHT = 1 shl 3
}
