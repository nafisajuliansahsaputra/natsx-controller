package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.SessionReadyPayloadCodec
import com.natsx.controller.core.protocol.TransportReadyPayload
import com.natsx.controller.core.protocol.TransportReadyPayloadCodec
import java.nio.ByteBuffer
import java.nio.ByteOrder

object UsbControlFrameCodec {
    fun encodeSessionReady(
        trustedSession: UsbTrustedSession,
        payload: SessionReadyPayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray =
        ProtocolFrameCodec.encode(
            ProtocolFrame(
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

    fun decodeSessionReady(
        bytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): SessionReadyPayload {
        val frame = decodeAuthenticated(bytes, trustedSession)

        require(frame.messageType == MessageType.SESSION_READY) {
            "Expected SESSION_READY."
        }

        return SessionReadyPayloadCodec.decode(frame.payload)
    }

    fun encodeTransportReady(
        trustedSession: UsbTrustedSession,
        monotonicTimestampMicros: ULong,
    ): ByteArray =
        ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.TRANSPORT_READY,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = TransportReadyPayloadCodec.encode(
                    TransportReadyPayload(
                        ProtocolTransport.USB_DIRECT,
                    ),
                ),
            ),
            authenticationKey = trustedSession.authenticationKey(),
        )

    fun decodeTransportReady(
        bytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): TransportReadyPayload {
        val frame = decodeAuthenticated(bytes, trustedSession)

        require(frame.messageType == MessageType.TRANSPORT_READY) {
            "Expected TRANSPORT_READY."
        }

        return TransportReadyPayloadCodec.decode(frame.payload)
    }

    fun encodeHeartbeatAck(
        trustedSession: UsbTrustedSession,
        responderTimestampMicros: ULong,
        echoedProbeTimestampMicros: ULong,
    ): ByteArray {
        val payload =
            ByteBuffer
                .allocate(8)
                .order(ByteOrder.LITTLE_ENDIAN)
                .putLong(echoedProbeTimestampMicros.toLong())
                .array()

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
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

    fun decodeHeartbeat(
        bytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): ULong {
        val frame = decodeAuthenticated(bytes, trustedSession)

        require(frame.messageType == MessageType.HEARTBEAT) {
            "Expected HEARTBEAT."
        }
        require(frame.payload.isEmpty()) {
            "HEARTBEAT payload must be empty."
        }

        return frame.monotonicTimestampMicros
    }

    private fun decodeAuthenticated(
        bytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): ProtocolFrame {
        val frame =
            ProtocolFrameCodec.decode(
                bytes,
                trustedSession.authenticationKey(),
            )

        require(
            frame.flags and FrameFlags.AUTHENTICATED != 0,
        ) {
            "USB session traffic must be authenticated."
        }
        require(frame.sessionId == trustedSession.sessionId) {
            "USB frame belongs to a different controller session."
        }

        return frame
    }
}
