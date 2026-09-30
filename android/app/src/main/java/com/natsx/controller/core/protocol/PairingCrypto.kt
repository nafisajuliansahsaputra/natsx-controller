package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object PairingCrypto {
    const val PAIRING_ROOT_KEY_SIZE = 32
    const val CONFIRMATION_TAG_SIZE = 16

    private val rootInfo =
        "NATSX-PAIRING-ROOT-V1"
            .toByteArray(Charsets.US_ASCII)

    private val sasPrefix =
        "NATSX-PAIRING-SAS-V1"
            .toByteArray(Charsets.US_ASCII)

    private val androidConfirmPrefix =
        "NATSX-ANDROID-CONFIRM-V1"
            .toByteArray(Charsets.US_ASCII)

    private val windowsConfirmPrefix =
        "NATSX-WINDOWS-CONFIRM-V1"
            .toByteArray(Charsets.US_ASCII)

    private val completePrefix =
        "NATSX-PAIRING-COMPLETE-V1"
            .toByteArray(Charsets.US_ASCII)

    fun createEphemeralKeyPair(): KeyPair {
        val generator = KeyPairGenerator.getInstance("EC")
        generator.initialize(ECGenParameterSpec("secp256r1"))
        return generator.generateKeyPair()
    }

    fun exportPublicKey(keyPair: KeyPair): ByteArray =
        keyPair.public.encoded.copyOf()

    fun computeTranscriptHash(
        pairRequest: ByteArray,
        pairResponse: ByteArray,
    ): ByteArray =
        MessageDigest.getInstance("SHA-256")
            .digest(pairRequest + pairResponse)

    fun derivePairingRootKey(
        localEphemeralKey: KeyPair,
        peerPublicKeyDer: ByteArray,
        transcriptHash: ByteArray,
    ): ByteArray {
        require(transcriptHash.size == 32)

        val peerPublicKey =
            KeyFactory.getInstance("EC")
                .generatePublic(
                    X509EncodedKeySpec(peerPublicKeyDer),
                )

        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(localEphemeralKey.private)
        agreement.doPhase(peerPublicKey, true)

        val sharedSecret = agreement.generateSecret()

        return try {
            hkdfSha256(
                ikm = sharedSecret,
                salt = transcriptHash,
                info = rootInfo,
                length = PAIRING_ROOT_KEY_SIZE,
            )
        } finally {
            sharedSecret.fill(0)
        }
    }

    fun computeSas(
        pairingRootKey: ByteArray,
        transcriptHash: ByteArray,
    ): String {
        val hash = computeHmac(
            pairingRootKey,
            sasPrefix,
            transcriptHash,
        )

        val value =
            ByteBuffer.wrap(hash, 0, 4)
                .order(ByteOrder.BIG_ENDIAN)
                .int
                .toUInt()
                .toLong()

        hash.fill(0)

        return (value % 1_000_000L)
            .toString()
            .padStart(6, '0')
    }

    fun computeConfirmationTag(
        role: PairingRole,
        pairingRootKey: ByteArray,
        transcriptHash: ByteArray,
    ): ByteArray {
        val prefix = when (role) {
            PairingRole.ANDROID -> androidConfirmPrefix
            PairingRole.WINDOWS -> windowsConfirmPrefix
        }

        val hash = computeHmac(
            pairingRootKey,
            prefix,
            transcriptHash,
        )

        return hash.copyOfRange(
            0,
            CONFIRMATION_TAG_SIZE,
        ).also {
            hash.fill(0)
        }
    }

    fun computeCompletionTag(
        pairingRootKey: ByteArray,
        transcriptHash: ByteArray,
    ): ByteArray {
        val hash = computeHmac(
            pairingRootKey,
            completePrefix,
            transcriptHash,
        )

        return hash.copyOfRange(
            0,
            CONFIRMATION_TAG_SIZE,
        ).also {
            hash.fill(0)
        }
    }

    fun verifyTag(
        expected: ByteArray,
        supplied: ByteArray,
    ): Boolean {
        if (expected.size != CONFIRMATION_TAG_SIZE ||
            supplied.size != CONFIRMATION_TAG_SIZE
        ) {
            return false
        }

        return MessageDigest.isEqual(expected, supplied)
    }

    private fun computeHmac(
        key: ByteArray,
        prefix: ByteArray,
        transcriptHash: ByteArray,
    ): ByteArray {
        require(key.size == PAIRING_ROOT_KEY_SIZE)
        require(transcriptHash.size == 32)

        val input = prefix + transcriptHash
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))

        return mac.doFinal(input).also {
            input.fill(0)
        }
    }

    private fun hkdfSha256(
        ikm: ByteArray,
        salt: ByteArray,
        info: ByteArray,
        length: Int,
    ): ByteArray {
        require(length in 1..32)

        val extract = Mac.getInstance("HmacSHA256")
        extract.init(SecretKeySpec(salt, "HmacSHA256"))
        val prk = extract.doFinal(ikm)

        val expand = Mac.getInstance("HmacSHA256")
        expand.init(SecretKeySpec(prk, "HmacSHA256"))
        val block = expand.doFinal(info + byteArrayOf(0x01))

        return block.copyOfRange(0, length).also {
            prk.fill(0)
            block.fill(0)
        }
    }
}
