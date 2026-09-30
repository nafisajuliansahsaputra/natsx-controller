package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class SessionReadyPayloadCodecTest {
    @Test
    fun roundTripPreservesIdentityAndCapabilities() {
        val peerId =
            PeerId.fromBytes(ByteArray(PeerId.SIZE) { (it + 1).toByte() })

        val expected = SessionReadyPayload(
            role = PeerRole.ANDROID_CONTROLLER,
            capabilities =
                TransportCapabilities.WIFI or
                    TransportCapabilities.BLUETOOTH,
            peerId = peerId,
        )

        val encoded = SessionReadyPayloadCodec.encode(expected)
        val decoded = SessionReadyPayloadCodec.decode(encoded)

        assertEquals(SessionReadyPayloadCodec.PAYLOAD_SIZE, encoded.size)
        assertEquals(expected.role, decoded.role)
        assertEquals(expected.capabilities, decoded.capabilities)
        assertArrayEquals(
            expected.peerId.toByteArray(),
            decoded.peerId.toByteArray(),
        )
        assertEquals(0, encoded[2].toInt())
        assertEquals(0, encoded[3].toInt())
    }

    @Test
    fun decodeRejectsNonZeroReservedBytes() {
        val encoded = SessionReadyPayloadCodec.encode(
            SessionReadyPayload(
                role = PeerRole.WINDOWS_RECEIVER,
                capabilities = TransportCapabilities.WIFI,
                peerId = PeerId.Zero,
            ),
        )

        encoded[3] = 1

        assertThrows(IllegalArgumentException::class.java) {
            SessionReadyPayloadCodec.decode(encoded)
        }
    }
}
