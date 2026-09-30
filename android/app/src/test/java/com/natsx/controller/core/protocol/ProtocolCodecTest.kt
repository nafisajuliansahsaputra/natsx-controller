package com.natsx.controller.core.protocol

import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocolCodecTest {
    private val sessionKey = hex(
        "000102030405060708090a0b0c0d0e0f" +
            "101112131415161718191a1b1c1d1e1f",
    )

    @Test
    fun crc32cMatchesStandardCastagnoliVector() {
        val crc = Crc32C.compute("123456789".toByteArray(Charsets.US_ASCII))
        assertEquals(0xE3069283u, crc.toUInt())
    }

    @Test
    fun canonicalGamepadPayloadMatchesSharedVector() {
        val state = canonicalState()

        assertEquals(
            "290205000080ff7fc7cf393011fa0000",
            GamepadStateCodec.encode(state).toHex(),
        )
    }

    @Test
    fun canonicalAuthenticatedFrameMatchesCsharpVector() {
        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.GAMEPAD_STATE,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = SessionId.fromBytes(
                hex("00112233445566778899aabbccddeeff"),
            ),
            sequence = 0x01020304u,
            monotonicTimestampMicros = 0x0102030405060708uL,
            payload = GamepadStateCodec.encode(canonicalState()),
        )

        val encoded = ProtocolFrameCodec.encode(frame, sessionKey)

        assertEquals(
            "4e584331010007012800100000112233445566778899aabbccddeeff040302010807060504030201290205000080ff7fc7cf393011fa0000200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
            encoded.toHex(),
        )
    }

    @Test
    fun canonicalAuthenticatedFrameDecodes() {
        val encoded = hex(
            "4e584331010007012800100000112233445566778899aabbccddeeff040302010807060504030201290205000080ff7fc7cf393011fa0000200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
        )

        val frame = ProtocolFrameCodec.decode(encoded, sessionKey)
        val state = GamepadStateCodec.decode(frame.payload)

        assertEquals(MessageType.GAMEPAD_STATE, frame.messageType)
        assertEquals(0x01020304u, frame.sequence)
        assertEquals(-32768, state.leftX)
        assertEquals(32767, state.leftY)
        assertEquals(250, state.rightTrigger)
        assertTrue(state.buttons and GamepadButtons.A != 0)
    }

    @Test(expected = IllegalArgumentException::class)
    fun tamperedFrameIsRejected() {
        val encoded = hex(
            "4e584331010007012800100000112233445566778899aabbccddeeff040302010807060504030201290205000080ff7fc7cf393011fa0000200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
        )

        encoded[ProtocolConstants.HEADER_SIZE + 4] =
            (encoded[ProtocolConstants.HEADER_SIZE + 4].toInt() xor 1).toByte()

        ProtocolFrameCodec.decode(encoded, sessionKey)
    }

    private fun canonicalState() = GamepadState(
        buttons =
            GamepadButtons.A or
                GamepadButtons.Y or
                GamepadButtons.RIGHT_SHOULDER or
                GamepadButtons.START,
        dpad = DpadState.UP or DpadState.LEFT,
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
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }
    }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
