package com.natsx.controller.core.transport.wifi

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class WifiDiscoveryProtocolTest {
    @Test
    fun requestHasCanonicalMagic() {
        val request = WifiDiscoveryProtocol.createRequest()

        assertEquals(8, request.size)
        assertEquals("NXCDISC1", request.decodeToString())
    }

    @Test
    fun responseDecodesCanonicalLayout() {
        val receiverId = ByteArray(16) { it.toByte() }
        val name = "NATSX-LEGION"
        val nameBytes = name.encodeToByteArray()

        val payload = ByteBuffer
            .allocate(WifiDiscoveryProtocol.FIXED_RESPONSE_SIZE + nameBytes.size)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put("NXCANN01".encodeToByteArray())
                put(1)
                put(0)
                putShort(42161.toShort())
                put(receiverId)
                put(nameBytes.size.toByte())
                put(nameBytes)
            }
            .array()

        val response = WifiDiscoveryProtocol.decodeResponse(payload)

        assertEquals(42161, response.realtimePort)
        assertArrayEquals(receiverId, response.receiverId)
        assertEquals(name, response.receiverName)
    }

    @Test
    fun corruptMagicIsRejected() {
        val payload = ByteArray(WifiDiscoveryProtocol.FIXED_RESPONSE_SIZE + 1)
        payload[28] = 1
        payload[29] = 'x'.code.toByte()

        assertThrows(IllegalArgumentException::class.java) {
            WifiDiscoveryProtocol.decodeResponse(payload)
        }
    }
}
