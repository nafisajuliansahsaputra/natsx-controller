package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class HelloPayloadCodecTest {
    @Test
    fun roundTripPreservesDiscoveryFields() {
        val peerId = PeerId.fromBytes(
            byteArrayOf(
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88.toByte(), 0x99.toByte(), 0xAA.toByte(), 0xBB.toByte(),
                0xCC.toByte(), 0xDD.toByte(), 0xEE.toByte(), 0xFF.toByte(),
            ),
        )

        val expected = HelloPayload(
            role = PeerRole.WINDOWS_RECEIVER,
            capabilities =
                TransportCapabilities.WIFI or
                    TransportCapabilities.BLUETOOTH or
                    TransportCapabilities.USB_DIRECT,
            peerId = peerId,
            realtimePort = 43860,
            discoveryNonce = 0x12345678u,
        )

        val bytes = HelloPayloadCodec.encode(expected)
        val decoded = HelloPayloadCodec.decode(bytes)

        assertEquals(HelloPayloadCodec.PAYLOAD_SIZE, bytes.size)
        assertEquals(expected, decoded)
    }

    @Test
    fun decodeRejectsUnknownCapabilities() {
        val bytes = ByteArray(HelloPayloadCodec.PAYLOAD_SIZE)
        bytes[0] = PeerRole.ANDROID_CONTROLLER.wireValue.toByte()
        bytes[1] = 0x80.toByte()

        assertThrows(IllegalArgumentException::class.java) {
            HelloPayloadCodec.decode(bytes)
        }
    }
}
