package com.natsx.controller.core.protocol

import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object TrustedSessionCrypto {
    const val PAIRING_ROOT_KEY_SIZE = 32
    const val CHALLENGE_SIZE = 32
    const val SESSION_KEY_SIZE = 32
    const val PROOF_SIZE = 16

    private val sessionInfoPrefix =
        "NATSX-CONTROLLER-V1".toByteArray(StandardCharsets.US_ASCII)

    private val androidProofPrefix =
        "NATSX-ANDROID-PROOF-V1".toByteArray(StandardCharsets.US_ASCII)

    fun deriveSessionKey(
        pairingRootKey: ByteArray,
        challenge: ByteArray,
        androidDeviceId: SessionId,
        windowsDeviceId: SessionId,
        sessionId: SessionId,
    ): ByteArray {
        require(pairingRootKey.size == PAIRING_ROOT_KEY_SIZE)
        require(challenge.size == CHALLENGE_SIZE)

        val info =
            sessionInfoPrefix +
                androidDeviceId.toByteArray() +
                windowsDeviceId.toByteArray() +
                sessionId.toByteArray()

        val prk = hmacSha256(challenge, pairingRootKey)
        val okm = hmacSha256(prk, info + byteArrayOf(0x01))

        val result = okm.copyOfRange(0, SESSION_KEY_SIZE)

        prk.fill(0)
        okm.fill(0)
        info.fill(0)

        return result
    }

    fun computeAndroidProof(
        sessionKey: ByteArray,
        androidHelloPayload: ByteArray,
        windowsHelloPayload: ByteArray,
        challenge: ByteArray,
        sessionId: SessionId,
    ): ByteArray {
        require(sessionKey.size == SESSION_KEY_SIZE)
        require(challenge.size == CHALLENGE_SIZE)

        val transcript = androidHelloPayload + windowsHelloPayload
        val transcriptHash = MessageDigest.getInstance("SHA-256").digest(transcript)

        val proofInput =
            androidProofPrefix +
                transcriptHash +
                challenge +
                sessionId.toByteArray()

        val hash = hmacSha256(sessionKey, proofInput)
        val proof = hash.copyOfRange(0, PROOF_SIZE)

        transcript.fill(0)
        transcriptHash.fill(0)
        proofInput.fill(0)
        hash.fill(0)

        return proof
    }

    fun verifyAndroidProof(
        expectedProof: ByteArray,
        suppliedProof: ByteArray,
    ): Boolean {
        if (expectedProof.size != PROOF_SIZE || suppliedProof.size != PROOF_SIZE) {
            return false
        }

        return MessageDigest.isEqual(expectedProof, suppliedProof)
    }

    private fun hmacSha256(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
