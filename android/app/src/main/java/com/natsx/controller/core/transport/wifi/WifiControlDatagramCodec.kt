package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.SessionReadyPayloadCodec
import java.nio.ByteBuffer
import java.nio.ByteOrder

object WifiControlDatagramCodec {
    private const val HEARTBEAT_ACK_PAYLOAD_SIZE = 8


    fun encodeSessionReady(
        trustedSession: WifiTrustedSession,
        payload: SessionReadyPayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        return ProtocolFrameCodec.encode(
            frame = ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.SESSION_READY,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = SessionReadyPayloadCodec.encode(payload),
            ),
            authenticationKey = trustedSession.authenticationKey(),
        )
    }

    fun decodeSessionReady(
        datagram: ByteArray,
        trustedSession: WifiTrustedSession,
    ): SessionReadyPayload {
        val frame = decodeAuthenticatedFrame(datagram, trustedSession)

        require(frame.messageType == MessageType.SESSION_READY) {
            "Expected SESSION_READY, received " + frame.messageType + "."
        }

        return SessionReadyPayloadCodec.decode(frame.payload)
    }

    fun encodeHeartbeat(
        trustedSession: WifiTrustedSession,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        return ProtocolFrameCodec.encode(
            frame = ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HEARTBEAT,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = byteArrayOf(),
            ),
            authenticationKey = trustedSession.authenticationKey(),
        )
    }

    fun decodeHeartbeat(
        datagram: ByteArray,
        trustedSession: WifiTrustedSession,
    ): ULong {
        val frame = decodeAuthenticatedFrame(datagram, trustedSession)

        require(frame.messageType == MessageType.HEARTBEAT) {
            "Expected HEARTBEAT, received " + frame.messageType + "."
        }
        require(frame.payload.isEmpty()) {
            "HEARTBEAT payload must be empty."
        }

        return frame.monotonicTimestampMicros
    }

    fun encodeHeartbeatAck(
        trustedSession: WifiTrustedSession,
        responderTimestampMicros: ULong,
        echoedProbeTimestampMicros: ULong,
    ): ByteArray {
        val payload =
            ByteBuffer
                .allocate(HEARTBEAT_ACK_PAYLOAD_SIZE)
                .order(ByteOrder.LITTLE_ENDIAN)
                .putLong(echoedProbeTimestampMicros.toLong())
                .array()

        return ProtocolFrameCodec.encode(
            frame = ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HEARTBEAT_ACK,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = responderTimestampMicros,
                payload = payload,
            ),
            authenticationKey = trustedSession.authenticationKey(),
        )
    }

    fun decodeHeartbeatAck(
        datagram: ByteArray,
        trustedSession: WifiTrustedSession,
    ): ULong {
        val frame = decodeAuthenticatedFrame(datagram, trustedSession)

        require(frame.messageType == MessageType.HEARTBEAT_ACK) {
            "Expected HEARTBEAT_ACK, received " + frame.messageType + "."
        }
        require(frame.payload.size == HEARTBEAT_ACK_PAYLOAD_SIZE) {
            "HEARTBEAT_ACK payload must be exactly 8 bytes."
        }

        return ByteBuffer
            .wrap(frame.payload)
            .order(ByteOrder.LITTLE_ENDIAN)
            .long
            .toULong()
    }

    private fun decodeAuthenticatedFrame(
        datagram: ByteArray,
        trustedSession: WifiTrustedSession,
    ): ProtocolFrame {
        val frame = ProtocolFrameCodec.decode(
            frameBytes = datagram,
            authenticationKey = trustedSession.authenticationKey(),
        )

        require(frame.flags and FrameFlags.AUTHENTICATED != 0) {
            "Wi-Fi session traffic must be authenticated."
        }
        require(frame.sessionId == trustedSession.sessionId) {
            "Wi-Fi frame belongs to a different controller session."
        }

        return frame
    }
}
