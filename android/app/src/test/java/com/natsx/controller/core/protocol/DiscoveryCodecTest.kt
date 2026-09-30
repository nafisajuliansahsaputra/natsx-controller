package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class DiscoveryCodecTest {
    @Test
    fun requestRoundTrips() {
        val id = SessionId.fromBytes(
            hex("00112233445566778899aabbccddeeff"),
        )

        val decoded = DiscoveryCodec.decodeRequest(
            DiscoveryCodec.encodeRequest(DiscoveryRequest(id)),
        )

        assertEquals(id, decoded.androidDeviceId)
    }

    @Test
    fun responseRoundTripsUtf8Name() {
        val id = SessionId.fromBytes(
            hex("ffeeddccbbaa99887766554433221100"),
        )
        val response = DiscoveryResponse(
            windowsDeviceId = id,
            controllerPort = 37074,
            receiverName = "NATSX-LEGION",
        )

        val decoded = DiscoveryCodec.decodeResponse(
            DiscoveryCodec.encodeResponse(response),
        )

        assertEquals(response, decoded)
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }
}
