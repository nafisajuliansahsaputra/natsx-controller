package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.SessionId
import org.junit.Assert.assertEquals
import org.junit.Test

class BluetoothRealtimeStreamEncoderTest {
    private val sessionKey =
        hex(
            "000102030405060708090a0b0c0d0e0f" +
                "101112131415161718191a1b1c1d1e1f",
        )

    @Test
    fun canonicalAuthenticatedStateMatchesCsharpFixture() {
        BluetoothTrustedSession(
            sessionId = SessionId.fromBytes(
                hex(
                    "00112233445566778899aabbccddeeff",
                ),
            ),
            sessionKey = sessionKey,
        ).use { session ->
            val encoded =
                BluetoothRealtimeStreamEncoder
                    .encodeGamepadState(
                        state = canonicalState(),
                        trustedSession = session,
                        sequence = 0x01020304u,
                        monotonicTimestampMicros =
                            0x0102030405060708uL,
                    )

            assertEquals(
                "4c00" +
                    "4e584331010007012800100000112233445566778899aabbccddeeff" +
                    "040302010807060504030201290205000080ff7fc7cf393011fa0000" +
                    "200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
                encoded.toHex(),
            )
        }
    }

    private fun canonicalState() =
        GamepadState(
            buttons =
                GamepadButtons.A or
                    GamepadButtons.Y or
                    GamepadButtons.RIGHT_SHOULDER or
                    GamepadButtons.START,
            dpad =
                DpadState.UP or
                    DpadState.LEFT,
            leftX = -32768,
            leftY = 32767,
            rightX = -12345,
            rightY = 12345,
            leftTrigger = 17,
            rightTrigger = 250,
        )

    private fun hex(value: String): ByteArray {
        require(value.length % 2 == 0)

        return ByteArray(value.length / 2) { index ->
            value
                .substring(
                    index * 2,
                    index * 2 + 2,
                )
                .toInt(16)
                .toByte()
        }
    }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(
                it.toInt() and 0xFF,
            )
        }
}
