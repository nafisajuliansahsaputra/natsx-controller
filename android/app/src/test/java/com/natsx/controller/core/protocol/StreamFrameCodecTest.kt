package com.natsx.controller.core.protocol

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import org.junit.Assert.assertEquals
import org.junit.Test

class StreamFrameCodecTest {
    @Test
    fun unauthenticatedFrameRoundTrips() {
        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HELLO,
            flags = FrameFlags.NONE,
            sessionId = SessionId.Zero,
            sequence = 0u,
            monotonicTimestampMicros = 42uL,
            payload = ControlPayloadCodec.encodeHello(
                HelloPayload(
                    deviceId = SessionId.fromBytes(
                        hex(
                            "00112233445566778899aabbccddeeff",
                        ),
                    ),
                    role = DeviceRole.ANDROID_CONTROLLER,
                    transports =
                        TransportMask.WIFI or
                            TransportMask.BLUETOOTH,
                    capabilities =
                        CapabilityFlags.WARM_STANDBY,
                    minimumMajor = 1,
                    maximumMajor = 1,
                    maximumMinor = 0,
                    trustState = TrustState.PAIRED,
                ),
            ),
        )

        val output = ByteArrayOutputStream()
        StreamFrameCodec.write(output, frame)

        val decoded = StreamFrameCodec.read(
            ByteArrayInputStream(output.toByteArray()),
        )

        assertEquals(frame.messageType, decoded.messageType)
        assertEquals(frame.payload.toList(), decoded.payload.toList())
    }

    @Test
    fun authenticatedFrameRoundTrips() {
        val key = ByteArray(32) { it.toByte() }
        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HEARTBEAT,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = SessionId.fromBytes(
                hex(
                    "00112233445566778899aabbccddeeff",
                ),
            ),
            sequence = 0u,
            monotonicTimestampMicros = 99uL,
            payload = ControlPayloadCodec.encodeHeartbeat(
                HeartbeatPayload(7u),
            ),
        )

        val output = ByteArrayOutputStream()
        StreamFrameCodec.write(
            output,
            frame,
            key,
        )

        val decoded = StreamFrameCodec.read(
            ByteArrayInputStream(output.toByteArray()),
            key,
        )

        assertEquals(frame.messageType, decoded.messageType)
        assertEquals(frame.sessionId, decoded.sessionId)
    }

    @Test(expected = IllegalArgumentException::class)
    fun invalidLengthIsRejected() {
        StreamFrameCodec.read(
            ByteArrayInputStream(
                byteArrayOf(1, 0),
            ),
        )
    }

    @Test(expected = EOFException::class)
    fun truncatedFrameThrowsEof() {
        StreamFrameCodec.read(
            ByteArrayInputStream(
                byteArrayOf(
                    44,
                    0,
                    1,
                    2,
                    3,
                ),
            ),
        )
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) { index ->
            value.substring(
                index * 2,
                index * 2 + 2,
            ).toInt(16).toByte()
        }
}
