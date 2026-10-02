package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.RumblePayload
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TransportPreferenceMode
import com.natsx.controller.core.protocol.TransportPreferencePayload
import org.junit.Assert.assertEquals
import org.junit.Test

class WifiControlDatagramCodecTest {
    private val key = ByteArray(32) { it.toByte() }
    private val sessionId = SessionId.fromBytes(ByteArray(16) { it.toByte() })

    @Test
    fun heartbeatAckEchoesWindowsProbeTimestamp() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val heartbeat = ProtocolFrameCodec.encode(
                frame = ProtocolFrame(
                    version = ProtocolVersion.Current,
                    messageType = MessageType.HEARTBEAT,
                    flags = FrameFlags.AUTHENTICATED,
                    sessionId = sessionId,
                    sequence = 0u,
                    monotonicTimestampMicros = 444_000uL,
                    payload = byteArrayOf(),
                ),
                authenticationKey = key,
            )

            val probe =
                WifiControlDatagramCodec.decodeHeartbeat(
                    heartbeat,
                    trusted,
                )

            assertEquals(444_000uL, probe)

            val ack =
                WifiControlDatagramCodec.encodeHeartbeatAck(
                    trustedSession = trusted,
                    responderTimestampMicros = 999_000uL,
                    echoedProbeTimestampMicros = probe,
                )

            val decodedEcho =
                WifiControlDatagramCodec.decodeHeartbeatAck(
                    ack,
                    trusted,
                )

            assertEquals(444_000uL, decodedEcho)
        }
    }
    @Test
    fun rumbleRoundTripPreservesMotorStrengths() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val expected =
                RumblePayload(
                    lowFrequencyMotor = 210,
                    highFrequencyMotor = 88,
                )

            val encoded =
                WifiControlDatagramCodec.encodeRumble(
                    trustedSession = trusted,
                    payload = expected,
                    monotonicTimestampMicros = 555uL,
                )

            assertEquals(
                expected,
                WifiControlDatagramCodec.decodeRumble(
                    encoded,
                    trusted,
                ),
            )
        }
    }

    @Test
    fun handoverCommitRoundTripPreservesAuthorityAndSequence() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val expected =
                HandoverPayload(
                    transport = ProtocolTransport.USB_DIRECT,
                    stateSequence = 0xCAFE_BABEu,
                )

            val encoded =
                WifiControlDatagramCodec.encodeHandoverCommit(
                    trustedSession = trusted,
                    payload = expected,
                    monotonicTimestampMicros = 777uL,
                )

            assertEquals(
                expected,
                WifiControlDatagramCodec.decodeHandoverCommit(
                    encoded,
                    trusted,
                ),
            )
        }
    }

    @Test
    fun transportPreferenceRoundTripPreservesMode() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val expected =
                TransportPreferencePayload(
                    TransportPreferenceMode.USB_DIRECT,
                )

            val encoded =
                WifiControlDatagramCodec
                    .encodeTransportPreference(
                        trustedSession = trusted,
                        payload = expected,
                        monotonicTimestampMicros = 778uL,
                    )

            assertEquals(
                expected,
                WifiControlDatagramCodec
                    .decodeTransportPreference(
                        encoded,
                        trusted,
                    ),
            )
        }
    }

    @Test
    fun heartbeatEncoderProducesAuthenticatedSessionProbe() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val encoded =
                WifiControlDatagramCodec.encodeHeartbeat(
                    trustedSession = trusted,
                    monotonicTimestampMicros = 123_456uL,
                )

            val decoded =
                WifiControlDatagramCodec.decodeHeartbeat(
                    encoded,
                    trusted,
                )

            assertEquals(123_456uL, decoded)
        }
    }

    @Test
    fun sessionReadyRoundTripPreservesTrustedIdentity() {
        WifiTrustedSession(sessionId, key).use { trusted ->
            val payload = SessionReadyPayload(
                role = PeerRole.ANDROID_CONTROLLER,
                capabilities =
                    TransportCapabilities.WIFI or
                        TransportCapabilities.BLUETOOTH,
                peerId =
                    PeerId.fromBytes(
                        ByteArray(PeerId.SIZE) { (it + 20).toByte() },
                    ),
            )

            val encoded =
                WifiControlDatagramCodec.encodeSessionReady(
                    trustedSession = trusted,
                    payload = payload,
                    monotonicTimestampMicros = 123_456uL,
                )

            val decoded =
                WifiControlDatagramCodec.decodeSessionReady(
                    encoded,
                    trusted,
                )

            assertEquals(payload.role, decoded.role)
            assertEquals(payload.capabilities, decoded.capabilities)
            assertEquals(payload.peerId, decoded.peerId)
        }
    }

}
