package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class ControlPayloadCodecTest {
    @Test
    fun helloRoundTrips() {
        val deviceId = SessionId.fromBytes(
            hex("00112233445566778899aabbccddeeff"),
        )
        val payload = HelloPayload(
            deviceId = deviceId,
            role = DeviceRole.ANDROID_CONTROLLER,
            transports = TransportMask.USB or TransportMask.WIFI or TransportMask.BLUETOOTH,
            capabilities =
                CapabilityFlags.RUMBLE or
                    CapabilityFlags.COMPETITIVE_240_HZ or
                    CapabilityFlags.WARM_STANDBY,
            minimumMajor = 1,
            maximumMajor = 1,
            maximumMinor = 0,
            trustState = TrustState.PAIRED,
        )

        val decoded = ControlPayloadCodec.decodeHello(
            ControlPayloadCodec.encodeHello(payload),
        )

        assertEquals(payload, decoded)
    }

    @Test
    fun heartbeatMatchesCsharpLittleEndianVector() {
        val encoded = ControlPayloadCodec.encodeHeartbeat(
            HeartbeatPayload(0x01020304u),
        )

        assertEquals("0403020100000000", encoded.toHex())
        assertEquals(
            0x01020304u,
            ControlPayloadCodec.decodeHeartbeat(encoded).probeId,
        )
    }

    @Test
    fun handoverPrepareRoundTrips() {
        val payload = HandoverPreparePayload(
            candidateTransport = 3,
            reason = HandoverReason.ACTIVE_TRANSPORT_CRITICAL_OR_LOST,
            expectedNextSequence = 0xAABBCCDDu,
        )

        assertEquals(
            payload,
            ControlPayloadCodec.decodeHandoverPrepare(
                ControlPayloadCodec.encodeHandoverPrepare(payload),
            ),
        )
    }

    @Test(expected = IllegalArgumentException::class)
    fun reservedHeartbeatBytesAreRejected() {
        val encoded = ControlPayloadCodec.encodeHeartbeat(HeartbeatPayload(1u))
        encoded[7] = 1
        ControlPayloadCodec.decodeHeartbeat(encoded)
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
