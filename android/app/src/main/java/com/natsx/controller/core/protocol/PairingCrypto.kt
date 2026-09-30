package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object PairingCrypto {
    const val NONCE_SIZE = 32
    const val P256_UNCOMPRESSED_PUBLIC_KEY_SIZE = 65
    const val DERIVED_KEY_SIZE = 32

    private val pairingContext = ascii("NATSX-PAIRING-V1")
    private val pairingKeyInfo = ascii("NATSX-PAIRING-KEY-V1")
    private val sasContext = ascii("NATSX-SAS-V1")
    private val pairingResponseContext = ascii("NATSX-PAIRING-RESPONSE-V1")
    private val trustSecretInfo = ascii("NATSX-TRUST-SECRET-V1")

    fun computePairingTranscriptHash(
        androidPeerId: PeerId,
        windowsPeerId: PeerId,
        androidNonce: ByteArray,
        windowsNonce: ByteArray,
        androidPublicKey: ByteArray,
        windowsPublicKey: ByteArray,
    ): ByteArray {
        validateNonce(androidNonce)
        validateNonce(windowsNonce)
        validatePublicKey(androidPublicKey)
        validatePublicKey(windowsPublicKey)

        val transcript =
            pairingContext +
                androidPeerId.toByteArray() +
                windowsPeerId.toByteArray() +
                androidNonce +
                windowsNonce +
                androidPublicKey +
                windowsPublicKey

        return try {
            MessageDigest.getInstance("SHA-256").digest(transcript)
        } finally {
            transcript.fill(0)
        }
    }

    fun derivePairingKey(
        ecdhSharedSecret: ByteArray,
        pairingTranscriptHash: ByteArray,
    ): ByteArray {
        validateHash(pairingTranscriptHash)

        return hkdfSha256Derive32(
            ikm = ecdhSharedSecret,
            salt = pairingTranscriptHash,
            info = pairingKeyInfo,
        )
    }

    fun deriveSixDigitSas(
        pairingKey: ByteArray,
        pairingTranscriptHash: ByteArray,
    ): String {
        validateKey(pairingKey)
        validateHash(pairingTranscriptHash)

        val input = sasContext + pairingTranscriptHash
        val mac = hmacSha256(pairingKey, input)

        return try {
            val value =
                ByteBuffer
                    .wrap(mac, 0, 4)
                    .order(ByteOrder.BIG_ENDIAN)
                    .int
                    .toUInt()

            (value % 1_000_000u)
                .toString()
                .padStart(6, '0')
        } finally {
            input.fill(0)
            mac.fill(0)
        }
    }


    fun computePairingResponseProof(
        pairingKey: ByteArray,
        pairingTranscriptHash: ByteArray,
        sessionId: SessionId,
    ): ByteArray {
        validateKey(pairingKey)
        validateHash(pairingTranscriptHash)

        val input =
            pairingResponseContext +
                pairingTranscriptHash +
                sessionId.toByteArray()

        return try {
            hmacSha256(pairingKey, input)
        } finally {
            input.fill(0)
        }
    }

    fun deriveTrustSecret(
        pairingKey: ByteArray,
        pairingTranscriptHash: ByteArray,
    ): ByteArray {
        validateKey(pairingKey)
        validateHash(pairingTranscriptHash)

        return hkdfSha256Derive32(
            ikm = pairingKey,
            salt = pairingTranscriptHash,
            info = trustSecretInfo,
        )
    }

    private fun hkdfSha256Derive32(
        ikm: ByteArray,
        salt: ByteArray,
        info: ByteArray,
    ): ByteArray {
        val prk = hmacSha256(salt, ikm)
        val expandInput = info + byteArrayOf(0x01)

        return try {
            hmacSha256(prk, expandInput)
        } finally {
            prk.fill(0)
            expandInput.fill(0)
        }
    }

    private fun hmacSha256(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }

    private fun validateNonce(nonce: ByteArray) {
        require(nonce.size == NONCE_SIZE) {
            "Nonce must be exactly $NONCE_SIZE bytes."
        }
    }

    private fun validatePublicKey(publicKey: ByteArray) {
        require(
            publicKey.size == P256_UNCOMPRESSED_PUBLIC_KEY_SIZE &&
                publicKey[0] == 0x04.toByte(),
        ) {
            "P-256 public key must use 65-byte uncompressed SEC1 encoding."
        }
    }

    private fun validateHash(hash: ByteArray) {
        require(hash.size == DERIVED_KEY_SIZE) {
            "SHA-256 hash must be exactly $DERIVED_KEY_SIZE bytes."
        }
    }

    private fun validateKey(key: ByteArray) {
        require(key.size == DERIVED_KEY_SIZE) {
            "Key must be exactly $DERIVED_KEY_SIZE bytes."
        }
    }

    private fun ascii(text: String): ByteArray =
        text.toByteArray(StandardCharsets.US_ASCII)
}
