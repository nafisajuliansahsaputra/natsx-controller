package com.natsx.controller.core.transport.usb

import android.os.SystemClock
import com.natsx.controller.core.protocol.AuthChallengePayload
import com.natsx.controller.core.protocol.AuthChallengePayloadCodec
import com.natsx.controller.core.protocol.AuthResponsePayload
import com.natsx.controller.core.protocol.AuthResponsePayloadCodec
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.HandoverPayloadCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.RumblePayload
import com.natsx.controller.core.protocol.RumblePayloadCodec
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.SessionReadyPayloadCodec
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TransportReadyPayload
import com.natsx.controller.core.protocol.TransportReadyPayloadCodec
import com.natsx.controller.core.protocol.TrustedReconnectCrypto
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.security.GeneralSecurityException

object UsbAuthFrameCodec {
    fun encodeChallenge(
        sessionId: SessionId,
        payload: AuthChallengePayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        require(sessionId != SessionId.Zero) {
            "USB trusted reconnect requires a non-zero session ID."
        }

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.AUTH_CHALLENGE,
                flags = FrameFlags.NONE,
                sessionId = sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = AuthChallengePayloadCodec.encode(payload),
            ),
        )
    }

    fun decodeResponse(
        frameBytes: ByteArray,
    ): Pair<SessionId, AuthResponsePayload> {
        val frame = ProtocolFrameCodec.decode(frameBytes)

        require(
            frame.messageType == MessageType.AUTH_RESPONSE &&
                frame.flags == FrameFlags.NONE &&
                frame.sessionId != SessionId.Zero,
        ) {
            "Invalid Usb AUTH_RESPONSE envelope."
        }

        return frame.sessionId to
            AuthResponsePayloadCodec.decode(frame.payload)
    }
}

object UsbControlFrameCodec {
    private const val HEARTBEAT_ACK_PAYLOAD_SIZE = 8

    fun encodeSessionReady(
        trustedSession: UsbTrustedSession,
        payload: SessionReadyPayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.SESSION_READY,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = SessionReadyPayloadCodec.encode(payload),
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )
    }

    fun decodeSessionReady(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): SessionReadyPayload {
        val frame =
            ProtocolFrameCodec.decode(
                frameBytes,
                trustedSession.authenticationKey(),
            )

        if (
            frame.messageType != MessageType.SESSION_READY ||
            frame.flags and FrameFlags.AUTHENTICATED == 0 ||
            frame.sessionId != trustedSession.sessionId
        ) {
            throw GeneralSecurityException(
                "Invalid authenticated Usb SESSION_READY frame.",
            )
        }

        return SessionReadyPayloadCodec.decode(frame.payload)
    }

    fun encodeRumble(
        trustedSession: UsbTrustedSession,
        payload: RumblePayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray =
        ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.RUMBLE,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload =
                    RumblePayloadCodec.encode(
                        payload,
                    ),
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )

    fun decodeRumble(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): RumblePayload {
        val frame =
            decodeAuthenticatedFrame(
                frameBytes,
                trustedSession,
            )

        require(
            frame.messageType ==
                MessageType.RUMBLE,
        ) {
            "Expected RUMBLE, received " +
                frame.messageType +
                "."
        }

        return RumblePayloadCodec.decode(
            frame.payload,
        )
    }

    fun encodeHandoverCommit(
        trustedSession: UsbTrustedSession,
        payload: HandoverPayload,
        monotonicTimestampMicros: ULong,
    ): ByteArray =
        ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType =
                    MessageType.HANDOVER_COMMIT,
                flags = FrameFlags.AUTHENTICATED,
                sessionId =
                    trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload =
                    HandoverPayloadCodec.encode(
                        payload,
                    ),
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )

    fun decodeHandoverCommit(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): HandoverPayload {
        val frame =
            decodeAuthenticatedFrame(
                frameBytes,
                trustedSession,
            )

        require(
            frame.messageType ==
                MessageType.HANDOVER_COMMIT,
        ) {
            "Expected HANDOVER_COMMIT, received " +
                frame.messageType +
                "."
        }

        return HandoverPayloadCodec.decode(
            frame.payload,
        )
    }

    fun encodeHeartbeat(
        trustedSession: UsbTrustedSession,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HEARTBEAT,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload = byteArrayOf(),
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )
    }

    fun decodeHeartbeat(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): ULong {
        val frame =
            decodeAuthenticatedFrame(
                frameBytes,
                trustedSession,
            )

        require(
            frame.messageType ==
                MessageType.HEARTBEAT,
        ) {
            "Expected HEARTBEAT, received " +
                frame.messageType +
                "."
        }

        require(frame.payload.isEmpty()) {
            "HEARTBEAT payload must be empty."
        }

        return frame.monotonicTimestampMicros
    }

    fun encodeHeartbeatAck(
        trustedSession: UsbTrustedSession,
        responderTimestampMicros: ULong,
        echoedProbeTimestampMicros: ULong,
    ): ByteArray {
        val payload =
            java.nio.ByteBuffer
                .allocate(
                    HEARTBEAT_ACK_PAYLOAD_SIZE,
                )
                .order(
                    java.nio.ByteOrder.LITTLE_ENDIAN,
                )
                .putLong(
                    echoedProbeTimestampMicros
                        .toLong(),
                )
                .array()

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType =
                    MessageType.HEARTBEAT_ACK,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros =
                    responderTimestampMicros,
                payload = payload,
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )
    }

    fun decodeHeartbeatAck(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): ULong {
        val frame =
            decodeAuthenticatedFrame(
                frameBytes,
                trustedSession,
            )

        require(
            frame.messageType ==
                MessageType.HEARTBEAT_ACK,
        ) {
            "Expected HEARTBEAT_ACK, received " +
                frame.messageType +
                "."
        }

        require(
            frame.payload.size ==
                HEARTBEAT_ACK_PAYLOAD_SIZE,
        ) {
            "HEARTBEAT_ACK payload must be exactly 8 bytes."
        }

        return java.nio.ByteBuffer
            .wrap(frame.payload)
            .order(
                java.nio.ByteOrder.LITTLE_ENDIAN,
            )
            .long
            .toULong()
    }

    fun encodeTransportReady(
        trustedSession: UsbTrustedSession,
        transport: ProtocolTransport,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType =
                    MessageType.TRANSPORT_READY,
                flags = FrameFlags.AUTHENTICATED,
                sessionId =
                    trustedSession.sessionId,
                sequence = 0u,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload =
                    TransportReadyPayloadCodec
                        .encode(
                            TransportReadyPayload(
                                transport,
                            ),
                        ),
            ),
            authenticationKey =
                trustedSession.authenticationKey(),
        )
    }

    fun decodeTransportReady(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): TransportReadyPayload {
        val frame =
            decodeAuthenticatedFrame(
                frameBytes,
                trustedSession,
            )

        require(
            frame.messageType ==
                MessageType.TRANSPORT_READY,
        ) {
            "Expected TRANSPORT_READY, received " +
                frame.messageType +
                "."
        }

        return TransportReadyPayloadCodec
            .decode(frame.payload)
    }

    private fun decodeAuthenticatedFrame(
        frameBytes: ByteArray,
        trustedSession: UsbTrustedSession,
    ): ProtocolFrame {
        val frame =
            ProtocolFrameCodec.decode(
                frameBytes,
                trustedSession.authenticationKey(),
            )

        require(
            frame.flags and
                FrameFlags.AUTHENTICATED != 0,
        ) {
            "USB session traffic must be authenticated."
        }

        require(
            frame.sessionId ==
                trustedSession.sessionId,
        ) {
            "USB frame belongs to a different controller session."
        }

        return frame
    }
}

class UsbTrustedHandshakeChallenge private constructor(
    val localPeerId: PeerId,
    val receiverPeerId: PeerId,
    val sessionId: SessionId,
    private val challengeNonce: ByteArray,
    private val trustSecret: ByteArray,
) : Closeable {
    private var closed = false

    fun encodeChallenge(
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        ensureOpen()

        return UsbAuthFrameCodec.encodeChallenge(
            sessionId = sessionId,
            payload = AuthChallengePayload(
                challengerPeerId = localPeerId,
                targetPeerId = receiverPeerId,
                challengeNonce = challengeNonce.copyOf(),
            ),
            monotonicTimestampMicros = monotonicTimestampMicros,
        )
    }

    fun acceptResponse(
        frameBytes: ByteArray,
    ): UsbTrustedSession {
        ensureOpen()

        val (responseSessionId, response) =
            UsbAuthFrameCodec.decodeResponse(
                frameBytes,
            )

        if (responseSessionId != sessionId) {
            throw GeneralSecurityException(
                "USB AUTH_RESPONSE belongs to a different session.",
            )
        }

        if (
            response.responderPeerId != receiverPeerId ||
            response.challengerPeerId != localPeerId
        ) {
            throw GeneralSecurityException(
                "USB AUTH_RESPONSE peer identity mismatch.",
            )
        }

        val valid =
            TrustedReconnectCrypto.verifyProof(
                trustKey = trustSecret,
                sessionId = sessionId,
                challengeNonce = challengeNonce,
                challengerPeerId = localPeerId,
                responderPeerId = receiverPeerId,
                suppliedProof = response.proof,
            )

        if (!valid) {
            throw GeneralSecurityException(
                "USB AUTH_RESPONSE proof is invalid.",
            )
        }

        val sessionKey =
            TrustedReconnectCrypto.deriveSessionKey(
                trustKey = trustSecret,
                sessionId = sessionId,
                challengeNonce = challengeNonce,
                challengerPeerId = localPeerId,
                responderPeerId = receiverPeerId,
            )

        return try {
            UsbTrustedSession(
                sessionId,
                sessionKey,
            )
        } finally {
            sessionKey.fill(0)
        }
    }

    override fun close() {
        if (closed) return

        challengeNonce.fill(0)
        trustSecret.fill(0)
        closed = true
    }

    private fun ensureOpen() {
        check(!closed) {
            "USB trusted handshake challenge is closed."
        }
    }

    companion object {
        fun create(
            localPeerId: PeerId,
            receiverPeerId: PeerId,
            trustSecret: ByteArray,
        ): UsbTrustedHandshakeChallenge {
            require(
                trustSecret.size ==
                    TrustedReconnectCrypto.TRUST_KEY_SIZE,
            ) {
                "Trust secret must be exactly " +
                    TrustedReconnectCrypto.TRUST_KEY_SIZE +
                    " bytes."
            }

            return UsbTrustedHandshakeChallenge(
                localPeerId = localPeerId,
                receiverPeerId = receiverPeerId,
                sessionId = SessionId.createRandom(),
                challengeNonce =
                    AuthChallengePayloadCodec.createChallenge(),
                trustSecret = trustSecret.copyOf(),
            )
        }
    }
}

class UsbTrustedReconnectClient(
    private val localPeerId: PeerId,
    private val sessionRegistry: TrustedSessionRegistry? = null,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos()
            .toULong() / 1_000uL
    },
) {
    fun connect(
        inputStream: InputStream,
        outputStream: OutputStream,
        receiverPeerId: PeerId,
        trustSecret: ByteArray,
    ): UsbTrustedSession {
        require(
            trustSecret.size ==
                TrustedReconnectCrypto.TRUST_KEY_SIZE,
        ) {
            "Trust secret must be exactly 32 bytes."
        }

        UsbTrustedHandshakeChallenge.create(
            localPeerId = localPeerId,
            receiverPeerId = receiverPeerId,
            trustSecret = trustSecret,
        ).use { challenge ->
            writeFrame(
                outputStream,
                challenge.encodeChallenge(
                    monotonicMicros(),
                ),
            )

            val trustedSession =
                challenge.acceptResponse(
                    UsbStreamFrameCodec.readFrame(
                        inputStream,
                    ),
                )

            try {
                writeFrame(
                    outputStream,
                    UsbControlFrameCodec
                        .encodeSessionReady(
                            trustedSession = trustedSession,
                            payload = SessionReadyPayload(
                                role =
                                    PeerRole.ANDROID_CONTROLLER,
                                capabilities =
                                    TransportCapabilities.WIFI or
                                        TransportCapabilities.BLUETOOTH or
                                        TransportCapabilities.USB_DIRECT,
                                peerId = localPeerId,
                            ),
                            monotonicTimestampMicros =
                                monotonicMicros(),
                        ),
                )

                val remoteReady =
                    UsbControlFrameCodec
                        .decodeSessionReady(
                            UsbStreamFrameCodec
                                .readFrame(inputStream),
                            trustedSession,
                        )

                if (
                    remoteReady.role !=
                    PeerRole.WINDOWS_RECEIVER ||
                    remoteReady.peerId !=
                    receiverPeerId ||
                    remoteReady.capabilities and
                        TransportCapabilities.USB_DIRECT == 0
                ) {
                    throw GeneralSecurityException(
                        "USB SESSION_READY does not match the trusted Windows receiver.",
                    )
                }

                sessionRegistry?.replace(
                    peerId = receiverPeerId,
                    sessionId = trustedSession.sessionId,
                    sessionKey =
                        trustedSession.authenticationKey(),
                )

                return trustedSession
            } catch (exception: Exception) {
                trustedSession.close()
                throw exception
            }
        }
    }

    private fun writeFrame(
        outputStream: OutputStream,
        frameBytes: ByteArray,
    ) {
        outputStream.write(
            UsbStreamFrameCodec.encode(
                frameBytes,
            ),
        )
        outputStream.flush()
    }
}
