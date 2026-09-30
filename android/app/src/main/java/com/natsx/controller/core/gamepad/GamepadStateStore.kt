package com.natsx.controller.core.gamepad

import java.util.concurrent.atomic.AtomicReference

class GamepadStateStore {
    private val state = AtomicReference(GamepadState.Neutral)

    fun snapshot(): GamepadState = state.get()

    fun setButton(mask: Int, pressed: Boolean) {
        update { current ->
            val nextButtons = if (pressed) {
                current.buttons or mask
            } else {
                current.buttons and mask.inv()
            }

            current.copy(buttons = nextButtons)
        }
    }

    fun setDpad(mask: Int, pressed: Boolean) {
        update { current ->
            var nextDpad = if (pressed) {
                current.dpad or mask
            } else {
                current.dpad and mask.inv()
            }

            if ((nextDpad and DpadState.UP) != 0 && (nextDpad and DpadState.DOWN) != 0) {
                nextDpad = nextDpad and DpadState.UP.inv() and DpadState.DOWN.inv()
            }

            if ((nextDpad and DpadState.LEFT) != 0 && (nextDpad and DpadState.RIGHT) != 0) {
                nextDpad = nextDpad and DpadState.LEFT.inv() and DpadState.RIGHT.inv()
            }

            current.copy(dpad = nextDpad)
        }
    }

    fun setLeftStick(x: Int, y: Int) {
        update { current ->
            current.copy(
                leftX = x.coerceIn(GamepadState.STICK_MIN, GamepadState.STICK_MAX),
                leftY = y.coerceIn(GamepadState.STICK_MIN, GamepadState.STICK_MAX),
            )
        }
    }

    fun setRightStick(x: Int, y: Int) {
        update { current ->
            current.copy(
                rightX = x.coerceIn(GamepadState.STICK_MIN, GamepadState.STICK_MAX),
                rightY = y.coerceIn(GamepadState.STICK_MIN, GamepadState.STICK_MAX),
            )
        }
    }

    fun setLeftTrigger(value: Int) {
        update { it.copy(leftTrigger = value.coerceIn(0, 255)) }
    }

    fun setRightTrigger(value: Int) {
        update { it.copy(rightTrigger = value.coerceIn(0, 255)) }
    }

    fun neutralize() {
        state.set(GamepadState.Neutral)
    }

    private inline fun update(transform: (GamepadState) -> GamepadState) {
        while (true) {
            val current = state.get()
            val next = transform(current)

            if (state.compareAndSet(current, next)) {
                return
            }
        }
    }
}
