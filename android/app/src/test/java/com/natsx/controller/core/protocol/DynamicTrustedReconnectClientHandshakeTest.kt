package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class DynamicTrustedReconnectClientHandshakeTest {
    private val androidId = SessionId.fromBytes(
        hex("00112233445566778899aabbccddeeff"),
    )

    private val windowsId = SessionId.fromBytes(
        hex("ffeeddccbbaa99887766554433221100"),
    )

    private val sessionId = SessionId.fromBytes(
        hex("102132435465768798a9bacbdcedfe0f"),
    )

    private val rootKey =
        ByteArray(32) { it.toByte() }

    private val challenge =
        ByteArray(32) { (it + 32).toByte() }

    @Test
    fun trustedWindowsIdentityIsResolvedAfterHello() {
        var resolvedId: SessionId? = null

        val client =
            DynamicTrustedReconnectClientHandshake(
                androidHello = androidHello(),
                trustResolver = { id ->
                    resolvedId = id
                    if (id == windowsId) {
                        rootKey.copyOf()
                    } else {
                        null
                    }
                },
                monotonicMicros = { 1234uL },
            )

        client.createHelloFrame()

        val identity =
            client.handleWindowsHello(
                windowsHelloFrame(),
            )

        assertEquals(windowsId, identity)
        assertEquals(windowsId, resolvedId)

        val proof =
            client.handleChallenge(
                ProtocolFrame(
                    version =
                        ProtocolVersion.Current,
                    messageType =
                        MessageType.AUTH_CHALLENGE,
                    flags = FrameFlags.NONE,
                    sessionId = sessionId,
                    sequence = 0u,
                    monotonicTimestampMicros = 2uL,
                    payload = challenge.copyOf(),
                ),
            )

        assertEquals(
            MessageType.AUTH_RESPONSE,
            proof.messageType,
        )
        assertEquals(
            "f087a2a2731e681b4d1a767396547c25",
            proof.payload.toHex(),
        )

        val key =
            client
                .sessionKeyForAuthenticatedDecode()

        val ready = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.SESSION_READY,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = sessionId,
            sequence = 0u,
            monotonicTimestampMicros = 3uL,
            payload =
                ControlPayloadCodec
                    .encodeSessionReady(
                        SessionReadyPayload(
                            selectedMajor = 1,
                            selectedMinor = 0,
                            currentTransport = 3,
                            negotiatedCapabilities =
                                CapabilityFlags.RUMBLE or
                                    CapabilityFlags.COMPETITIVE_240_HZ or
                                    CapabilityFlags.WARM_STANDBY,
                        ),
                    ),
        )

        val authenticated =
            ProtocolFrameCodec.decode(
                ProtocolFrameCodec.encode(
                    ready,
                    key,
                ),
                key,
            )

        val session =
            client.handleSessionReady(
                authenticated,
            )

        assertEquals(
            windowsId,
            session.windowsDeviceId,
        )
        assertEquals(
            sessionId,
            session.sessionId,
        )

        key.fill(0)
        session.sessionKey.fill(0)
        client.close()
    }

    @Test(expected = SecurityException::class)
    fun unknownWindowsIdentityIsRejected() {
        val client =
            DynamicTrustedReconnectClientHandshake(
                androidHello = androidHello(),
                trustResolver = { null },
            )

        client.createHelloFrame()
        client.handleWindowsHello(
            windowsHelloFrame(),
        )
    }

    private fun androidHello() =
        HelloPayload(
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

    private fun windowsHelloFrame(): ProtocolFrame {
        val payload = HelloPayload(
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

        return ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HELLO,
            flags = FrameFlags.NONE,
            sessionId = SessionId.Zero,
            sequence = 0u,
            monotonicTimestampMicros = 1uL,
            payload =
                ControlPayloadCodec
                    .encodeHello(payload),
        )
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) {
            index ->
            value.substring(
                index * 2,
                index * 2 + 2,
            ).toInt(16).toByte()
        }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(
                it.toInt() and 0xFF,
            )
        }
}
