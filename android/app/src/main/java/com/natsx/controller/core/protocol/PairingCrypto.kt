package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object PairingCrypto {
    const val SECRET_SIZE = 32
    const val NONCE_SIZE = 16
    const val PROOF_SIZE = 16
    const val DERIVED_KEY_SIZE = 32

    private const val HMAC_ALGORITHM = "HmacSHA256"

    private val PAIR_REQUEST_LABEL = "NATSX-PAIR-REQUEST-V1".encodeToByteArray()
    private val TRUST_LABEL = "NATSX-TRUST-V1".encodeToByteArray()
    private val PAIR_RESPONSE_LABEL = "NATSX-PAIR-RESPONSE-V1".encodeToByteArray()
    private val AUTH_CHALLENGE_LABEL = "NATSX-AUTH-CHALLENGE-V1".encodeToByteArray()
    private val SESSION_LABEL = "NATSX-SESSION-V1".encodeToByteArray()
    private val AUTH_RESPONSE_LABEL = "NATSX-AUTH-RESPONSE-V1".encodeToByteArray()
    private val SESSION_READY_LABEL = "NATSX-SESSION-READY-V1".encodeToByteArray()

    fun createPairRequestProof(
        pairingSecret: ByteArray,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
        expiresUnixSeconds: Long,
    ): ByteArray {
        validateSecret(pairingSecret)
        validateNonce(clientNonce)

        val expiry = ByteBuffer
            .allocate(Long.SIZE_BYTES)
            .order(ByteOrder.LITTLE_ENDIAN)
            .putLong(expiresUnixSeconds)
            .array()

        return truncatedHmac(
            pairingSecret,
            concat(
                PAIR_REQUEST_LABEL,
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
                clientNonce,
                expiry,
            ),
        )
    }

    fun deriveTrustKey(
        pairingSecret: ByteArray,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
        serverNonce: ByteArray,
    ): ByteArray {
        validateSecret(pairingSecret)
        validateNonce(clientNonce)
        validateNonce(serverNonce)

        return hkdfSha256(
            inputKey = pairingSecret,
            salt = concat(clientNonce, serverNonce),
            info = concat(
                TRUST_LABEL,
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
            ),
        )
    }

    fun createPairResponseProof(
        trustKey: ByteArray,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
        serverNonce: ByteArray,
    ): ByteArray {
        validateDerivedKey(trustKey)
        validateNonce(clientNonce)
        validateNonce(serverNonce)

        return truncatedHmac(
            trustKey,
            concat(
                PAIR_RESPONSE_LABEL,
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
                clientNonce,
                serverNonce,
            ),
        )
    }

    fun createAuthChallengeProof(
        trustKey: ByteArray,
        sessionId: SessionId,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
    ): ByteArray {
        validateDerivedKey(trustKey)
        validateNonce(clientNonce)

        return truncatedHmac(
            trustKey,
            concat(
                AUTH_CHALLENGE_LABEL,
                sessionId.toByteArray(),
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
                clientNonce,
            ),
        )
    }

    fun createAuthResponseProof(
        trustKey: ByteArray,
        sessionId: SessionId,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
        serverNonce: ByteArray,
    ): ByteArray {
        validateDerivedKey(trustKey)
        validateNonce(clientNonce)
        validateNonce(serverNonce)

        return truncatedHmac(
            trustKey,
            concat(
                AUTH_RESPONSE_LABEL,
                sessionId.toByteArray(),
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
                clientNonce,
                serverNonce,
            ),
        )
    }

    fun deriveSessionKey(
        trustKey: ByteArray,
        sessionId: SessionId,
        receiverPeerId: PeerId,
        controllerPeerId: PeerId,
        clientNonce: ByteArray,
        serverNonce: ByteArray,
    ): ByteArray {
        validateDerivedKey(trustKey)
        validateNonce(clientNonce)
        validateNonce(serverNonce)

        return hkdfSha256(
            inputKey = trustKey,
            salt = concat(clientNonce, serverNonce),
            info = concat(
                SESSION_LABEL,
                sessionId.toByteArray(),
                receiverPeerId.toByteArray(),
                controllerPeerId.toByteArray(),
            ),
        )
    }

    fun createSessionReadyProof(
        sessionKey: ByteArray,
        sessionId: SessionId,
    ): ByteArray {
        validateDerivedKey(sessionKey)

        return truncatedHmac(
            sessionKey,
            concat(SESSION_READY_LABEL, sessionId.toByteArray()),
        )
    }

    fun fixedTimeEquals(expected: ByteArray, actual: ByteArray): Boolean =
        expected.size == actual.size &&
            MessageDigest.isEqual(expected, actual)

    private fun hkdfSha256(
        inputKey: ByteArray,
        salt: ByteArray,
        info: ByteArray,
    ): ByteArray {
        val prk = hmac(salt, inputKey)

        return try {
            val block = hmac(
                prk,
                concat(info, byteArrayOf(1)),
            )
            block.copyOf(DERIVED_KEY_SIZE)
        } finally {
            prk.fill(0)
        }
    }

    private fun truncatedHmac(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray {
        val full = hmac(key, data)

        return try {
            full.copyOf(PROOF_SIZE)
        } finally {
            full.fill(0)
        }
    }

    private fun hmac(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray {
        val mac = Mac.getInstance(HMAC_ALGORITHM)
        mac.init(SecretKeySpec(key, HMAC_ALGORITHM))
        return mac.doFinal(data)
    }

    private fun concat(vararg values: ByteArray): ByteArray {
        val totalSize = values.sumOf { it.size }
        val output = ByteArray(totalSize)
        var offset = 0

        values.forEach { value ->
            value.copyInto(output, destinationOffset = offset)
            offset += value.size
        }

        return output
    }

    private fun validateSecret(secret: ByteArray) {
        require(secret.size == SECRET_SIZE) {
            "Pairing secret must be exactly $SECRET_SIZE bytes."
        }
    }

    private fun validateNonce(nonce: ByteArray) {
        require(nonce.size == NONCE_SIZE) {
            "Nonce must be exactly $NONCE_SIZE bytes."
        }
    }

    private fun validateDerivedKey(key: ByteArray) {
        require(key.size == DERIVED_KEY_SIZE) {
            "Key must be exactly $DERIVED_KEY_SIZE bytes."
        }
    }
}
