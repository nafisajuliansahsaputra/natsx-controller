package com.natsx.controller.core.protocol

enum class AuthenticationMode(val wireValue: Int) {
    FIRST_PAIRING(1),
    TRUSTED_RECONNECT(2);

    companion object {
        fun fromWireValue(value: Int): AuthenticationMode? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class AuthChallengePayload(
    val mode: AuthenticationMode,
    val senderPeerId: PeerId,
    val nonce: ByteArray,
    val sessionId: SessionId,
    val ephemeralPublicKey: ByteArray,
)

data class AuthResponsePayload(
    val mode: AuthenticationMode,
    val senderPeerId: PeerId,
    val nonce: ByteArray,
    val sessionId: SessionId,
    val ephemeralPublicKey: ByteArray,
    val proof: ByteArray,
)

object AuthPayloadCodec {
    const val CHALLENGE_PAYLOAD_SIZE = 133
    const val RESPONSE_PAYLOAD_SIZE = 165

    private const val MODE_OFFSET = 0
    private const val PEER_ID_OFFSET = 4
    private const val NONCE_OFFSET = 20
    private const val SESSION_ID_OFFSET = 52
    private const val PUBLIC_KEY_OFFSET = 68
    private const val PROOF_OFFSET = 133

    fun encodeChallenge(payload: AuthChallengePayload): ByteArray {
        validateCommon(
            payload.mode,
            payload.senderPeerId,
            payload.nonce,
            payload.sessionId,
            payload.ephemeralPublicKey,
        )

        return ByteArray(CHALLENGE_PAYLOAD_SIZE).also { bytes ->
            bytes[MODE_OFFSET] = payload.mode.wireValue.toByte()
            payload.senderPeerId.toByteArray().copyInto(bytes, PEER_ID_OFFSET)
            payload.nonce.copyInto(bytes, NONCE_OFFSET)
            payload.sessionId.toByteArray().copyInto(bytes, SESSION_ID_OFFSET)
            payload.ephemeralPublicKey.copyInto(bytes, PUBLIC_KEY_OFFSET)
        }
    }

    fun decodeChallenge(bytes: ByteArray): AuthChallengePayload {
        require(bytes.size == CHALLENGE_PAYLOAD_SIZE) {
            "AUTH_CHALLENGE payload must be exactly $CHALLENGE_PAYLOAD_SIZE bytes."
        }
        validateReserved(bytes)

        val payload = AuthChallengePayload(
            mode =
                AuthenticationMode.fromWireValue(
                    bytes[MODE_OFFSET].toInt() and 0xFF,
                ) ?: throw IllegalArgumentException(
                    "Unknown authentication mode.",
                ),
            senderPeerId =
                PeerId.fromBytes(
                    bytes.copyOfRange(
                        PEER_ID_OFFSET,
                        PEER_ID_OFFSET + PeerId.SIZE,
                    ),
                ),
            nonce =
                bytes.copyOfRange(
                    NONCE_OFFSET,
                    NONCE_OFFSET + PairingCrypto.NONCE_SIZE,
                ),
            sessionId =
                SessionId.fromBytes(
                    bytes.copyOfRange(
                        SESSION_ID_OFFSET,
                        SESSION_ID_OFFSET + SessionId.SIZE,
                    ),
                ),
            ephemeralPublicKey =
                bytes.copyOfRange(
                    PUBLIC_KEY_OFFSET,
                    PUBLIC_KEY_OFFSET +
                        PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
                ),
        )

        validateCommon(
            payload.mode,
            payload.senderPeerId,
            payload.nonce,
            payload.sessionId,
            payload.ephemeralPublicKey,
        )
        return payload
    }

    fun encodeResponse(payload: AuthResponsePayload): ByteArray {
        validateCommon(
            payload.mode,
            payload.senderPeerId,
            payload.nonce,
            payload.sessionId,
            payload.ephemeralPublicKey,
        )
        require(payload.proof.size == PairingCrypto.DERIVED_KEY_SIZE) {
            "AUTH_RESPONSE proof must be exactly 32 bytes."
        }

        return ByteArray(RESPONSE_PAYLOAD_SIZE).also { bytes ->
            bytes[MODE_OFFSET] = payload.mode.wireValue.toByte()
            payload.senderPeerId.toByteArray().copyInto(bytes, PEER_ID_OFFSET)
            payload.nonce.copyInto(bytes, NONCE_OFFSET)
            payload.sessionId.toByteArray().copyInto(bytes, SESSION_ID_OFFSET)
            payload.ephemeralPublicKey.copyInto(bytes, PUBLIC_KEY_OFFSET)
            payload.proof.copyInto(bytes, PROOF_OFFSET)
        }
    }

    fun decodeResponse(bytes: ByteArray): AuthResponsePayload {
        require(bytes.size == RESPONSE_PAYLOAD_SIZE) {
            "AUTH_RESPONSE payload must be exactly $RESPONSE_PAYLOAD_SIZE bytes."
        }
        validateReserved(bytes)

        val payload = AuthResponsePayload(
            mode =
                AuthenticationMode.fromWireValue(
                    bytes[MODE_OFFSET].toInt() and 0xFF,
                ) ?: throw IllegalArgumentException(
                    "Unknown authentication mode.",
                ),
            senderPeerId =
                PeerId.fromBytes(
                    bytes.copyOfRange(
                        PEER_ID_OFFSET,
                        PEER_ID_OFFSET + PeerId.SIZE,
                    ),
                ),
            nonce =
                bytes.copyOfRange(
                    NONCE_OFFSET,
                    NONCE_OFFSET + PairingCrypto.NONCE_SIZE,
                ),
            sessionId =
                SessionId.fromBytes(
                    bytes.copyOfRange(
                        SESSION_ID_OFFSET,
                        SESSION_ID_OFFSET + SessionId.SIZE,
                    ),
                ),
            ephemeralPublicKey =
                bytes.copyOfRange(
                    PUBLIC_KEY_OFFSET,
                    PUBLIC_KEY_OFFSET +
                        PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
                ),
            proof =
                bytes.copyOfRange(
                    PROOF_OFFSET,
                    PROOF_OFFSET + PairingCrypto.DERIVED_KEY_SIZE,
                ),
        )

        validateCommon(
            payload.mode,
            payload.senderPeerId,
            payload.nonce,
            payload.sessionId,
            payload.ephemeralPublicKey,
        )
        return payload
    }

    private fun validateCommon(
        mode: AuthenticationMode,
        senderPeerId: PeerId,
        nonce: ByteArray,
        sessionId: SessionId,
        ephemeralPublicKey: ByteArray,
    ) {
        require(senderPeerId != PeerId.Zero) {
            "Sender Peer ID must not be zero."
        }
        require(nonce.size == PairingCrypto.NONCE_SIZE) {
            "Authentication nonce must be exactly 32 bytes."
        }
        require(sessionId != SessionId.Zero) {
            "Session ID must not be zero."
        }
        require(
            ephemeralPublicKey.size ==
                PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
        ) {
            "Authentication public-key field must be exactly 65 bytes."
        }

        when (mode) {
            AuthenticationMode.FIRST_PAIRING ->
                require(ephemeralPublicKey[0] == 0x04.toByte()) {
                    "First-pairing public key must use uncompressed SEC1 encoding."
                }

            AuthenticationMode.TRUSTED_RECONNECT ->
                require(ephemeralPublicKey.all { it == 0.toByte() }) {
                    "Trusted reconnect must zero the ephemeral public-key field."
                }
        }
    }

    private fun validateReserved(bytes: ByteArray) {
        require(
            bytes[1] == 0.toByte() &&
                bytes[2] == 0.toByte() &&
                bytes[3] == 0.toByte(),
        ) {
            "Authentication reserved bytes must be zero."
        }
    }
}
