package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TrustedReconnectClientHandshakeTest {
    private val androidId = SessionId.fromBytes(
        hex("00112233445566778899aabbccddeeff"),
    )

    private val windowsId = SessionId.fromBytes(
        hex("ffeeddccbbaa99887766554433221100"),
    )

    private val sessionId = SessionId.fromBytes(
        hex("102132435465768798a9bacbdcedfe0f"),
    )

    private val rootKey = ByteArray(32) { it.toByte() }
    private val challenge = ByteArray(32) { (it + 32).toByte() }

    @Test
    fun validTrustedReconnectProducesSharedProofAndAcceptsSessionReady() {
        val androidHello = HelloPayload(
            deviceId = androidId,
            role = DeviceRole.ANDROID_CONTROLLER,
            transports =
                TransportMask.USB or
                    TransportMask.WIFI or
                    TransportMask.BLUETOOTH,
            capabilities =
                CapabilityFlags.RUMBLE or
                    CapabilityFlags.COMPETITIVE_240_HZ or
                    CapabilityFlags.WARM_STANDBY,
            minimumMajor = 1,
            maximumMajor = 1,
            maximumMinor = 0,
            trustState = TrustState.PAIRED,
        )

        val windowsHello = HelloPayload(
            deviceId = windowsId,
            role = DeviceRole.WINDOWS_RECEIVER,
            transports =
                TransportMask.USB or
                    TransportMask.WIFI or
                    TransportMask.BLUETOOTH,
            capabilities =
                CapabilityFlags.RUMBLE or
                    CapabilityFlags.COMPETITIVE_240_HZ or
                    CapabilityFlags.WARM_STANDBY,
            minimumMajor = 1,
            maximumMajor = 1,
            maximumMinor = 0,
            trustState = TrustState.PAIRED,
        )

        val client = TrustedReconnectClientHandshake(
            androidHello = androidHello,
            expectedWindowsDeviceId = windowsId,
            pairingRootKey = rootKey,
            monotonicMicros = { 1234uL },
        )

        val hello = client.createHelloFrame()
        assertEquals(MessageType.HELLO, hello.messageType)
        assertEquals(SessionId.Zero, hello.sessionId)

        client.handleWindowsHello(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HELLO,
                flags = FrameFlags.NONE,
                sessionId = SessionId.Zero,
                sequence = 0u,
                monotonicTimestampMicros = 100uL,
                payload = ControlPayloadCodec.encodeHello(windowsHello),
            ),
        )

        val proofFrame = client.handleChallenge(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.AUTH_CHALLENGE,
                flags = FrameFlags.NONE,
                sessionId = sessionId,
                sequence = 0u,
                monotonicTimestampMicros = 200uL,
                payload = challenge.copyOf(),
            ),
        )

        assertEquals(MessageType.AUTH_RESPONSE, proofFrame.messageType)
        assertEquals(
            "f087a2a2731e681b4d1a767396547c25",
            proofFrame.payload.toHex(),
        )

        val key = client.sessionKeyForAuthenticatedDecode()

        val ready = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.SESSION_READY,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = sessionId,
            sequence = 0u,
            monotonicTimestampMicros = 300uL,
            payload = ControlPayloadCodec.encodeSessionReady(
                SessionReadyPayload(
                    selectedMajor = 1,
                    selectedMinor = 0,
                    currentTransport = 2,
                    negotiatedCapabilities =
                        CapabilityFlags.RUMBLE or
                            CapabilityFlags.COMPETITIVE_240_HZ or
                            CapabilityFlags.WARM_STANDBY,
                ),
            ),
        )

        val encoded = ProtocolFrameCodec.encode(ready, key)
        val decoded = ProtocolFrameCodec.decode(encoded, key)

        val established = client.handleSessionReady(decoded)

        assertEquals(TrustedReconnectClientState.ACTIVE, client.state)
        assertEquals(sessionId, established.sessionId)
        assertTrue(
            established.negotiatedCapabilities and CapabilityFlags.RUMBLE != 0,
        )
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
