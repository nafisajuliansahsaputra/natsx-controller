package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

data class PairRequestPayload(
    val controllerPeerId: PeerId,
    val clientNonce: ByteArray,
    val expiresUnixSeconds: Long,
    val proof: ByteArray,
)

data class PairResponsePayload(
    val serverNonce: ByteArray,
    val proof: ByteArray,
)

data class AuthChallengePayload(
    val controllerPeerId: PeerId,
    val clientNonce: ByteArray,
    val proof: ByteArray,
)

data class AuthResponsePayload(
    val serverNonce: ByteArray,
    val proof: ByteArray,
)

data class SessionReadyPayload(
    val proof: ByteArray,
)

object PairingPayloadCodec {
    const val PAIR_REQUEST_SIZE =
        PeerId.SIZE + PairingCrypto.NONCE_SIZE + Long.SIZE_BYTES + PairingCrypto.PROOF_SIZE
    const val PAIR_RESPONSE_SIZE =
        PairingCrypto.NONCE_SIZE + PairingCrypto.PROOF_SIZE
    const val AUTH_CHALLENGE_SIZE =
        PeerId.SIZE + PairingCrypto.NONCE_SIZE + PairingCrypto.PROOF_SIZE
    const val AUTH_RESPONSE_SIZE =
        PairingCrypto.NONCE_SIZE + PairingCrypto.PROOF_SIZE
    const val SESSION_READY_SIZE = PairingCrypto.PROOF_SIZE

    fun encodePairRequest(payload: PairRequestPayload): ByteArray {
        validateNonce(payload.clientNonce)
        validateProof(payload.proof)

        return ByteBuffer
            .allocate(PAIR_REQUEST_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(payload.controllerPeerId.toByteArray())
                put(payload.clientNonce)
                putLong(payload.expiresUnixSeconds)
                put(payload.proof)
            }
            .array()
    }

    fun decodePairRequest(bytes: ByteArray): PairRequestPayload {
        requireSize(bytes, PAIR_REQUEST_SIZE, "PairRequestPayload")
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val peer = ByteArray(PeerId.SIZE)
        val nonce = ByteArray(PairingCrypto.NONCE_SIZE)
        val proof = ByteArray(PairingCrypto.PROOF_SIZE)

        buffer.get(peer)
        buffer.get(nonce)
        val expires = buffer.long
        buffer.get(proof)

        return PairRequestPayload(
            controllerPeerId = PeerId.fromBytes(peer),
            clientNonce = nonce,
            expiresUnixSeconds = expires,
            proof = proof,
        )
    }

    fun encodePairResponse(payload: PairResponsePayload): ByteArray {
        validateNonce(payload.serverNonce)
        validateProof(payload.proof)
        return payload.serverNonce + payload.proof
    }

    fun decodePairResponse(bytes: ByteArray): PairResponsePayload {
        requireSize(bytes, PAIR_RESPONSE_SIZE, "PairResponsePayload")
        return PairResponsePayload(
            serverNonce = bytes.copyOfRange(0, PairingCrypto.NONCE_SIZE),
            proof = bytes.copyOfRange(PairingCrypto.NONCE_SIZE, bytes.size),
        )
    }

    fun encodeAuthChallenge(payload: AuthChallengePayload): ByteArray {
        validateNonce(payload.clientNonce)
        validateProof(payload.proof)
        return payload.controllerPeerId.toByteArray() +
            payload.clientNonce +
            payload.proof
    }

    fun decodeAuthChallenge(bytes: ByteArray): AuthChallengePayload {
        requireSize(bytes, AUTH_CHALLENGE_SIZE, "AuthChallengePayload")
        return AuthChallengePayload(
            controllerPeerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            clientNonce = bytes.copyOfRange(
                PeerId.SIZE,
                PeerId.SIZE + PairingCrypto.NONCE_SIZE,
            ),
            proof = bytes.copyOfRange(
                PeerId.SIZE + PairingCrypto.NONCE_SIZE,
                bytes.size,
            ),
        )
    }

    fun encodeAuthResponse(payload: AuthResponsePayload): ByteArray {
        validateNonce(payload.serverNonce)
        validateProof(payload.proof)
        return payload.serverNonce + payload.proof
    }

    fun decodeAuthResponse(bytes: ByteArray): AuthResponsePayload {
        requireSize(bytes, AUTH_RESPONSE_SIZE, "AuthResponsePayload")
        return AuthResponsePayload(
            serverNonce = bytes.copyOfRange(0, PairingCrypto.NONCE_SIZE),
            proof = bytes.copyOfRange(PairingCrypto.NONCE_SIZE, bytes.size),
        )
    }

    fun encodeSessionReady(payload: SessionReadyPayload): ByteArray {
        validateProof(payload.proof)
        return payload.proof.copyOf()
    }

    fun decodeSessionReady(bytes: ByteArray): SessionReadyPayload {
        requireSize(bytes, SESSION_READY_SIZE, "SessionReadyPayload")
        return SessionReadyPayload(bytes.copyOf())
    }

    private fun validateNonce(value: ByteArray) {
        require(value.size == PairingCrypto.NONCE_SIZE) {
            "Nonce must be exactly ${PairingCrypto.NONCE_SIZE} bytes."
        }
    }

    private fun validateProof(value: ByteArray) {
        require(value.size == PairingCrypto.PROOF_SIZE) {
            "Proof must be exactly ${PairingCrypto.PROOF_SIZE} bytes."
        }
    }

    private fun requireSize(
        value: ByteArray,
        expected: Int,
        name: String,
    ) {
        require(value.size == expected) {
            "$name payload must be exactly $expected bytes."
        }
    }
}
