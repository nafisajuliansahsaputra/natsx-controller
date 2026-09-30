package com.natsx.controller.core.protocol

import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

data class AuthChallengePayload(
    val challengerPeerId: PeerId,
    val targetPeerId: PeerId,
    val challengeNonce: ByteArray,
)

object AuthChallengePayloadCodec {
    const val CHALLENGE_SIZE = 32
    const val PAYLOAD_SIZE = PeerId.SIZE + PeerId.SIZE + CHALLENGE_SIZE

    fun encode(payload: AuthChallengePayload): ByteArray {
        require(payload.challengeNonce.size == CHALLENGE_SIZE) {
            "Authentication challenge must be exactly $CHALLENGE_SIZE bytes."
        }

        return ByteArray(PAYLOAD_SIZE).also { out ->
            payload.challengerPeerId.toByteArray().copyInto(out, 0)
            payload.targetPeerId.toByteArray().copyInto(out, PeerId.SIZE)
            payload.challengeNonce.copyInto(out, PeerId.SIZE * 2)
        }
    }

    fun decode(bytes: ByteArray): AuthChallengePayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "AUTH_CHALLENGE payload must be exactly $PAYLOAD_SIZE bytes."
        }

        return AuthChallengePayload(
            challengerPeerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            targetPeerId = PeerId.fromBytes(bytes.copyOfRange(PeerId.SIZE, PeerId.SIZE * 2)),
            challengeNonce = bytes.copyOfRange(PeerId.SIZE * 2, PAYLOAD_SIZE),
        )
    }

    fun createChallenge(): ByteArray =
        ByteArray(CHALLENGE_SIZE).also { SecureRandom().nextBytes(it) }
}

data class AuthResponsePayload(
    val responderPeerId: PeerId,
    val challengerPeerId: PeerId,
    val proof: ByteArray,
)

object AuthResponsePayloadCodec {
    const val PROOF_SIZE = 32
    const val PAYLOAD_SIZE = PeerId.SIZE + PeerId.SIZE + PROOF_SIZE

    fun encode(payload: AuthResponsePayload): ByteArray {
        require(payload.proof.size == PROOF_SIZE) {
            "Authentication proof must be exactly $PROOF_SIZE bytes."
        }

        return ByteArray(PAYLOAD_SIZE).also { out ->
            payload.responderPeerId.toByteArray().copyInto(out, 0)
            payload.challengerPeerId.toByteArray().copyInto(out, PeerId.SIZE)
            payload.proof.copyInto(out, PeerId.SIZE * 2)
        }
    }

    fun decode(bytes: ByteArray): AuthResponsePayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "AUTH_RESPONSE payload must be exactly $PAYLOAD_SIZE bytes."
        }

        return AuthResponsePayload(
            responderPeerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            challengerPeerId = PeerId.fromBytes(bytes.copyOfRange(PeerId.SIZE, PeerId.SIZE * 2)),
            proof = bytes.copyOfRange(PeerId.SIZE * 2, PAYLOAD_SIZE),
        )
    }
}

object TrustedReconnectCrypto {
    const val TRUST_KEY_SIZE = 32
    const val SESSION_KEY_SIZE = 32

    private val proofLabel = "NATSX-AUTH-V1".toByteArray(Charsets.US_ASCII)
    private val sessionLabel = "NATSX-SESSION-V1".toByteArray(Charsets.US_ASCII)

    fun createProof(
        trustKey: ByteArray,
        sessionId: SessionId,
        challengeNonce: ByteArray,
        challengerPeerId: PeerId,
        responderPeerId: PeerId,
    ): ByteArray {
        validateInputs(trustKey, challengeNonce)

        val transcript =
            proofLabel +
                sessionId.toByteArray() +
                challengeNonce +
                challengerPeerId.toByteArray() +
                responderPeerId.toByteArray()

        val proof = hmacSha256(trustKey, transcript)
        transcript.fill(0)
        return proof
    }

    fun verifyProof(
        trustKey: ByteArray,
        sessionId: SessionId,
        challengeNonce: ByteArray,
        challengerPeerId: PeerId,
        responderPeerId: PeerId,
        suppliedProof: ByteArray,
    ): Boolean {
        if (suppliedProof.size != AuthResponsePayloadCodec.PROOF_SIZE) return false

        val expected = createProof(
            trustKey,
            sessionId,
            challengeNonce,
            challengerPeerId,
            responderPeerId,
        )

        val valid = MessageDigest.isEqual(expected, suppliedProof)
        expected.fill(0)
        return valid
    }

    fun deriveSessionKey(
        trustKey: ByteArray,
        sessionId: SessionId,
        challengeNonce: ByteArray,
        challengerPeerId: PeerId,
        responderPeerId: PeerId,
    ): ByteArray {
        validateInputs(trustKey, challengeNonce)

        val salt = challengeNonce + sessionId.toByteArray()
        val info =
            sessionLabel +
                challengerPeerId.toByteArray() +
                responderPeerId.toByteArray()

        val prk = hmacSha256(salt, trustKey)
        val expandInput = info + byteArrayOf(0x01)
        val sessionKey = hmacSha256(prk, expandInput)

        prk.fill(0)
        salt.fill(0)
        info.fill(0)
        expandInput.fill(0)

        return sessionKey
    }

    private fun validateInputs(
        trustKey: ByteArray,
        challengeNonce: ByteArray,
    ) {
        require(trustKey.size == TRUST_KEY_SIZE) {
            "Trust key must be exactly $TRUST_KEY_SIZE bytes."
        }
        require(challengeNonce.size == AuthChallengePayloadCodec.CHALLENGE_SIZE) {
            "Challenge nonce must be exactly ${AuthChallengePayloadCodec.CHALLENGE_SIZE} bytes."
        }
    }

    private fun hmacSha256(key: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
