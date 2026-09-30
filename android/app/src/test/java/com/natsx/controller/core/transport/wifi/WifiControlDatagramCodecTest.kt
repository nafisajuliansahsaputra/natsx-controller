package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId
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

}
