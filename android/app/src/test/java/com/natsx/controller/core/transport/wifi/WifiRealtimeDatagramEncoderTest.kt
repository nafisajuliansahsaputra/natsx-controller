package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.SessionId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class WifiRealtimeDatagramEncoderTest {
    private val key = ByteArray(32) { it.toByte() }
    private val sessionId = SessionId.fromBytes(
        byteArrayOf(
            0x00, 0x11, 0x22, 0x33,
            0x44, 0x55, 0x66, 0x77,
            0x88.toByte(), 0x99.toByte(), 0xAA.toByte(), 0xBB.toByte(),
            0xCC.toByte(), 0xDD.toByte(), 0xEE.toByte(), 0xFF.toByte(),
        ),
    )

    @Test
    fun encodeGamepadStateProducesAuthenticatedFrame() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val state = GamepadState(
                buttons = GamepadButtons.A or GamepadButtons.RIGHT_SHOULDER,
                dpad = DpadState.UP or DpadState.LEFT,
                leftX = -1000,
                leftY = 2000,
                rightX = -3000,
                rightY = 4000,
                leftTrigger = 17,
                rightTrigger = 250,
            )

            val bytes = WifiRealtimeDatagramEncoder.encodeGamepadState(
                state = state,
                trustedSession = trusted,
                sequence = 42u,
                monotonicTimestampMicros = 123456uL,
            )

            val frame = ProtocolFrameCodec.decode(bytes, key)

            assertEquals(sessionId, frame.sessionId)
            assertEquals(42u, frame.sequence)
            assertEquals(123456uL, frame.monotonicTimestampMicros)
        }
    }

    @Test
    fun trustedSessionRejectsShortKey() {
        assertThrows(IllegalArgumentException::class.java) {
            WifiTrustedSession(sessionId, ByteArray(16))
        }
    }

    @Test
    fun closedTrustedSessionCannotEncode() {
        val trusted = WifiTrustedSession(sessionId, key)
        trusted.close()

        assertThrows(IllegalStateException::class.java) {
            WifiRealtimeDatagramEncoder.encodeGamepadState(
                state = GamepadState.Neutral,
                trustedSession = trusted,
                sequence = 1u,
                monotonicTimestampMicros = 1uL,
            )
        }
    }
}
