package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.AuthChallengePayload
import com.natsx.controller.core.protocol.AuthChallengePayloadCodec
import com.natsx.controller.core.protocol.AuthResponsePayload
import com.natsx.controller.core.protocol.AuthResponsePayloadCodec
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedReconnectCrypto
import java.io.Closeable
import java.security.GeneralSecurityException

data class WifiAuthChallenge(
    val sessionId: SessionId,
    val payload: AuthChallengePayload,
)

data class WifiAuthResponse(
    val sessionId: SessionId,
    val payload: AuthResponsePayload,
)

object WifiAuthDatagramCodec {
    fun encodeChallenge(
        challenge: WifiAuthChallenge,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        require(challenge.sessionId != SessionId.Zero) {
            "Trusted reconnect requires a non-zero session ID."
        }

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.AUTH_CHALLENGE,
                flags = FrameFlags.NONE,
                sessionId = challenge.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = AuthChallengePayloadCodec.encode(challenge.payload),
            ),
        )
    }

    fun decodeChallenge(datagram: ByteArray): WifiAuthChallenge {
        val frame = ProtocolFrameCodec.decode(datagram)

        require(
            frame.messageType == MessageType.AUTH_CHALLENGE &&
                frame.flags == FrameFlags.NONE &&
                frame.sessionId != SessionId.Zero,
        ) {
            "Invalid Wi-Fi AUTH_CHALLENGE envelope."
        }

        return WifiAuthChallenge(
            sessionId = frame.sessionId,
            payload = AuthChallengePayloadCodec.decode(frame.payload),
        )
    }

    fun encodeResponse(
        response: WifiAuthResponse,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        require(response.sessionId != SessionId.Zero) {
            "Trusted reconnect requires a non-zero session ID."
        }

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.AUTH_RESPONSE,
                flags = FrameFlags.NONE,
                sessionId = response.sessionId,
                sequence = 0u,
                monotonicTimestampMicros = monotonicTimestampMicros,
                payload = AuthResponsePayloadCodec.encode(response.payload),
            ),
        )
    }

    fun decodeResponse(datagram: ByteArray): WifiAuthResponse {
        val frame = ProtocolFrameCodec.decode(datagram)

        require(
            frame.messageType == MessageType.AUTH_RESPONSE &&
                frame.flags == FrameFlags.NONE &&
                frame.sessionId != SessionId.Zero,
        ) {
            "Invalid Wi-Fi AUTH_RESPONSE envelope."
        }

        return WifiAuthResponse(
            sessionId = frame.sessionId,
            payload = AuthResponsePayloadCodec.decode(frame.payload),
        )
    }
}

class WifiTrustedHandshakeChallenge private constructor(
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

        return WifiAuthDatagramCodec.encodeChallenge(
            challenge = WifiAuthChallenge(
                sessionId = sessionId,
                payload = AuthChallengePayload(
                    challengerPeerId = localPeerId,
                    targetPeerId = receiverPeerId,
                    challengeNonce = challengeNonce.copyOf(),
                ),
            ),
            monotonicTimestampMicros = monotonicTimestampMicros,
        )
    }

    fun acceptResponse(
        datagram: ByteArray,
    ): WifiTrustedSession {
        ensureOpen()

        val response = WifiAuthDatagramCodec.decodeResponse(datagram)

        if (response.sessionId != sessionId) {
            throw GeneralSecurityException(
                "AUTH_RESPONSE belongs to a different session.",
            )
        }

        if (
            response.payload.responderPeerId != receiverPeerId ||
            response.payload.challengerPeerId != localPeerId
        ) {
            throw GeneralSecurityException(
                "AUTH_RESPONSE peer identity mismatch.",
            )
        }

        val valid = TrustedReconnectCrypto.verifyProof(
            trustKey = trustSecret,
            sessionId = sessionId,
            challengeNonce = challengeNonce,
            challengerPeerId = localPeerId,
            responderPeerId = receiverPeerId,
            suppliedProof = response.payload.proof,
        )

        if (!valid) {
            throw GeneralSecurityException(
                "AUTH_RESPONSE proof is invalid.",
            )
        }

        val sessionKey = TrustedReconnectCrypto.deriveSessionKey(
            trustKey = trustSecret,
            sessionId = sessionId,
            challengeNonce = challengeNonce,
            challengerPeerId = localPeerId,
            responderPeerId = receiverPeerId,
        )

        return try {
            WifiTrustedSession(sessionId, sessionKey)
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
            "Wi-Fi trusted handshake challenge is closed."
        }
    }

    companion object {
        fun create(
            localPeerId: PeerId,
            receiverPeerId: PeerId,
            trustSecret: ByteArray,
        ): WifiTrustedHandshakeChallenge {
            require(
                trustSecret.size == TrustedReconnectCrypto.TRUST_KEY_SIZE,
            ) {
                "Trust secret must be exactly " +
                    TrustedReconnectCrypto.TRUST_KEY_SIZE +
                    " bytes."
            }

            return WifiTrustedHandshakeChallenge(
                localPeerId = localPeerId,
                receiverPeerId = receiverPeerId,
                sessionId = SessionId.createRandom(),
                challengeNonce = AuthChallengePayloadCodec.createChallenge(),
                trustSecret = trustSecret.copyOf(),
            )
        }
    }
}

data class WifiTrustedHandshakeResponse(
    val remotePeerId: PeerId,
    val responseDatagram: ByteArray,
    val session: WifiTrustedSession,
) : Closeable {
    override fun close() {
        session.close()
        responseDatagram.fill(0)
    }
}

class WifiTrustedHandshakeResponder(
    private val localPeerId: PeerId,
) {
    fun handleChallenge(
        datagram: ByteArray,
        trustSecret: ByteArray,
        monotonicTimestampMicros: ULong,
    ): WifiTrustedHandshakeResponse {
        val challenge = WifiAuthDatagramCodec.decodeChallenge(datagram)

        if (challenge.payload.targetPeerId != localPeerId) {
            throw GeneralSecurityException(
                "AUTH_CHALLENGE target does not match this receiver.",
            )
        }

        val proof = TrustedReconnectCrypto.createProof(
            trustKey = trustSecret,
            sessionId = challenge.sessionId,
            challengeNonce = challenge.payload.challengeNonce,
            challengerPeerId = localPeerId,
            responderPeerId = challenge.payload.challengerPeerId,
        )

        val sessionKey = TrustedReconnectCrypto.deriveSessionKey(
            trustKey = trustSecret,
            sessionId = challenge.sessionId,
            challengeNonce = challenge.payload.challengeNonce,
            challengerPeerId = localPeerId,
            responderPeerId = challenge.payload.challengerPeerId,
        )

        try {
            val response = WifiAuthDatagramCodec.encodeResponse(
                response = WifiAuthResponse(
                    sessionId = challenge.sessionId,
                    payload = AuthResponsePayload(
                        responderPeerId = localPeerId,
                        challengerPeerId = challenge.payload.challengerPeerId,
                        proof = proof,
                    ),
                ),
                monotonicTimestampMicros = monotonicTimestampMicros,
            )

            return WifiTrustedHandshakeResponse(
                remotePeerId = challenge.payload.challengerPeerId,
                responseDatagram = response,
                session = WifiTrustedSession(
                    challenge.sessionId,
                    sessionKey,
                ),
            )
        } finally {
            proof.fill(0)
            sessionKey.fill(0)
            challenge.payload.challengeNonce.fill(0)
        }
    }
}
