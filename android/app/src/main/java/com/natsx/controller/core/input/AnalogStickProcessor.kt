package com.natsx.controller.core.input

import com.natsx.controller.core.gamepad.GamepadState
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.pow
import kotlin.math.roundToInt

data class StickOutput(
    val x: Int,
    val y: Int,
)

class AnalogStickProcessor(
    private val deadzone: Float = 0.05f,
    private val sensitivity: Float = 1f,
    private val jitterThreshold: Int = 96,
) {
    private var lastOutput = StickOutput(0, 0)

    init {
        require(deadzone in 0f..<1f)
        require(sensitivity in 0.5f..1.5f)
        require(jitterThreshold >= 0)
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
            lastOutput = StickOutput(0, 0)
            return lastOutput
        }

        val clampedMagnitude = magnitude.coerceAtMost(1f)
        val scaledMagnitude =
            ((clampedMagnitude - deadzone) / (1f - deadzone)).coerceIn(0f, 1f)
        val responseMagnitude =
            scaledMagnitude
                .pow(1f / sensitivity)
                .coerceIn(0f, 1f)

        val unitX = rawX / magnitude
        val unitY = rawY / magnitude

        val candidate = StickOutput(
            x = toStickRange(unitX * responseMagnitude),
            y = toStickRange(unitY * responseMagnitude),
        )

        if (
            abs(candidate.x - lastOutput.x) <= jitterThreshold &&
            abs(candidate.y - lastOutput.y) <= jitterThreshold
        ) {
            return lastOutput
        }

        lastOutput = candidate
        return candidate
    }

    fun reset() {
        lastOutput = StickOutput(0, 0)
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
