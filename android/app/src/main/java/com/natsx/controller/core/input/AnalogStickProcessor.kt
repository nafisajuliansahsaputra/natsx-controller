package com.natsx.controller.core.input

import com.natsx.controller.core.gamepad.GamepadState
import kotlin.math.hypot
import kotlin.math.roundToInt

data class StickOutput(
    val x: Int,
    val y: Int,
)

class AnalogStickProcessor(
    private val deadzone: Float = 0.05f,
) {
    init {
        require(deadzone in 0f..<1f)
    }

    fun process(
        pointerX: Float,
        pointerY: Float,
        centerX: Float,
        centerY: Float,
        radius: Float,
    ): StickOutput {
        require(radius > 0f)

        val rawX = (pointerX - centerX) / radius
        val rawY = -(pointerY - centerY) / radius
        val magnitude = hypot(rawX, rawY)

        if (magnitude <= deadzone) {
            return StickOutput(0, 0)
        }

        val clampedMagnitude = magnitude.coerceAtMost(1f)
        val scaledMagnitude =
            ((clampedMagnitude - deadzone) / (1f - deadzone)).coerceIn(0f, 1f)

        val unitX = rawX / magnitude
        val unitY = rawY / magnitude

        return StickOutput(
            x = toStickRange(unitX * scaledMagnitude),
            y = toStickRange(unitY * scaledMagnitude),
        )
    }

    private fun toStickRange(value: Float): Int {
        val clamped = value.coerceIn(-1f, 1f)

        return if (clamped >= 0f) {
            (clamped * GamepadState.STICK_MAX).roundToInt()
        } else {
            (clamped * -GamepadState.STICK_MIN).roundToInt()
        }.coerceIn(GamepadState.STICK_MIN, GamepadState.STICK_MAX)
    }
}
