package com.natsx.controller.core.protocol

import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadState
import java.nio.ByteBuffer
import java.nio.ByteOrder

object GamepadStateCodec {
    private const val ALLOWED_BUTTON_MASK = 0x07FF
    private const val ALLOWED_DPAD_MASK = 0x0F

    fun encode(state: GamepadState): ByteArray {
        validate(state)

        return ByteBuffer
            .allocate(ProtocolConstants.GAMEPAD_STATE_PAYLOAD_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                putShort(state.buttons.toShort())
                put(state.dpad.toByte())
                put(0)
                putShort(state.leftX.toShort())
                putShort(state.leftY.toShort())
                putShort(state.rightX.toShort())
                putShort(state.rightY.toShort())
                put(state.leftTrigger.toByte())
                put(state.rightTrigger.toByte())
                putShort(0)
            }
            .array()
    }

    fun decode(payload: ByteArray): GamepadState {
        require(payload.size == ProtocolConstants.GAMEPAD_STATE_PAYLOAD_SIZE) {
            "GAMEPAD_STATE payload must be exactly 16 bytes."
        }

        require(payload[3].toInt() == 0 && payload[14].toInt() == 0 && payload[15].toInt() == 0) {
            "Reserved GAMEPAD_STATE bytes must be zero."
        }

        val buffer = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        val buttons = buffer.short.toInt() and 0xFFFF
        val dpad = buffer.get().toInt() and 0xFF
        buffer.get()

        require(buttons and ALLOWED_BUTTON_MASK.inv() == 0) {
            "Reserved button bits must be zero."
        }

        require(dpad and ALLOWED_DPAD_MASK.inv() == 0) {
            "Reserved D-pad bits must be zero."
        }

        validateDpad(dpad)

        return GamepadState(
            buttons = buttons,
            dpad = dpad,
            leftX = buffer.short.toInt(),
            leftY = buffer.short.toInt(),
            rightX = buffer.short.toInt(),
            rightY = buffer.short.toInt(),
            leftTrigger = buffer.get().toInt() and 0xFF,
            rightTrigger = buffer.get().toInt() and 0xFF,
        )
    }

    private fun validate(state: GamepadState) {
        require(state.buttons and ALLOWED_BUTTON_MASK.inv() == 0) {
            "Gamepad state contains reserved button bits."
        }

        require(state.dpad and ALLOWED_DPAD_MASK.inv() == 0) {
            "Gamepad state contains reserved D-pad bits."
        }

        validateDpad(state.dpad)
    }

    private fun validateDpad(dpad: Int) {
        val verticalConflict =
            dpad and DpadState.UP != 0 &&
                dpad and DpadState.DOWN != 0

        val horizontalConflict =
            dpad and DpadState.LEFT != 0 &&
                dpad and DpadState.RIGHT != 0

        require(!verticalConflict && !horizontalConflict) {
            "Opposite D-pad directions are invalid."
        }
    }
}
