package com.natsx.controller.core.protocol

import java.math.BigInteger
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.AlgorithmParameters
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

enum class PairingRole(val wireValue: Int) {
    INITIATOR(1),
    RESPONDER(2);

    companion object {
        fun fromWireValue(value: Int): PairingRole? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class PairingOfferPayload(
    val initiatorPeerId: PeerId,
    val initiatorNonce: ByteArray,
    val initiatorPublicKey: ByteArray,
)

data class PairingResponsePayload(
    val responderPeerId: PeerId,
    val responderNonce: ByteArray,
    val responderPublicKey: ByteArray,
)

object PairingExchangePayloadCodec {
    const val NONCE_SIZE = 32
    const val UNCOMPRESSED_P256_PUBLIC_KEY_SIZE = 65
    const val PAYLOAD_SIZE =
        PeerId.SIZE + NONCE_SIZE + UNCOMPRESSED_P256_PUBLIC_KEY_SIZE

    fun encodeOffer(payload: PairingOfferPayload): ByteArray {
        validateNonce(payload.initiatorNonce)
        validatePublicKey(payload.initiatorPublicKey)

        return ByteArray(PAYLOAD_SIZE).also { out ->
            payload.initiatorPeerId.toByteArray().copyInto(out, 0)
            payload.initiatorNonce.copyInto(out, PeerId.SIZE)
            payload.initiatorPublicKey.copyInto(out, PeerId.SIZE + NONCE_SIZE)
        }
    }

    fun decodeOffer(bytes: ByteArray): PairingOfferPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "PAIRING_OFFER payload must be exactly $PAYLOAD_SIZE bytes."
        }

        val key = bytes.copyOfRange(
            PeerId.SIZE + NONCE_SIZE,
            PAYLOAD_SIZE,
        )
        validatePublicKey(key)

        return PairingOfferPayload(
            initiatorPeerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            initiatorNonce = bytes.copyOfRange(PeerId.SIZE, PeerId.SIZE + NONCE_SIZE),
            initiatorPublicKey = key,
        )
    }

    fun encodeResponse(payload: PairingResponsePayload): ByteArray {
        validateNonce(payload.responderNonce)
        validatePublicKey(payload.responderPublicKey)

        return ByteArray(PAYLOAD_SIZE).also { out ->
            payload.responderPeerId.toByteArray().copyInto(out, 0)
            payload.responderNonce.copyInto(out, PeerId.SIZE)
            payload.responderPublicKey.copyInto(out, PeerId.SIZE + NONCE_SIZE)
        }
    }

    fun decodeResponse(bytes: ByteArray): PairingResponsePayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "PAIRING_RESPONSE payload must be exactly $PAYLOAD_SIZE bytes."
        }

        val key = bytes.copyOfRange(
            PeerId.SIZE + NONCE_SIZE,
            PAYLOAD_SIZE,
        )
        validatePublicKey(key)

        return PairingResponsePayload(
            responderPeerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            responderNonce = bytes.copyOfRange(PeerId.SIZE, PeerId.SIZE + NONCE_SIZE),
            responderPublicKey = key,
        )
    }

    fun createNonce(): ByteArray =
        ByteArray(NONCE_SIZE).also { SecureRandom().nextBytes(it) }

    fun validatePublicKey(publicKey: ByteArray) {
        require(
            publicKey.size == UNCOMPRESSED_P256_PUBLIC_KEY_SIZE &&
                publicKey[0] == 0x04.toByte(),
        ) {
            "Pairing public key must be a 65-byte uncompressed P-256 point."
        }
    }

    private fun validateNonce(nonce: ByteArray) {
        require(nonce.size == NONCE_SIZE) {
            "Pairing nonce must be exactly $NONCE_SIZE bytes."
        }
    }
}

data class PairingConfirmPayload(
    val peerId: PeerId,
    val role: PairingRole,
    val proof: ByteArray,
)

object PairingConfirmPayloadCodec {
    const val PROOF_SIZE = 32
    const val PAYLOAD_SIZE = PeerId.SIZE + 1 + PROOF_SIZE

    fun encode(payload: PairingConfirmPayload): ByteArray {
        require(payload.proof.size == PROOF_SIZE) {
            "Pairing confirmation proof must be exactly $PROOF_SIZE bytes."
        }

        return ByteArray(PAYLOAD_SIZE).also { out ->
            payload.peerId.toByteArray().copyInto(out, 0)
            out[PeerId.SIZE] = payload.role.wireValue.toByte()
            payload.proof.copyInto(out, PeerId.SIZE + 1)
        }
    }

    fun decode(bytes: ByteArray): PairingConfirmPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "PAIRING_CONFIRM payload must be exactly $PAYLOAD_SIZE bytes."
        }

        val role = PairingRole.fromWireValue(bytes[PeerId.SIZE].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown pairing role.")

        return PairingConfirmPayload(
            peerId = PeerId.fromBytes(bytes.copyOfRange(0, PeerId.SIZE)),
            role = role,
            proof = bytes.copyOfRange(PeerId.SIZE + 1, PAYLOAD_SIZE),
        )
    }
}

enum class PairingAbortReason(val wireValue: Int) {
    USER_REJECTED(1),
    CODE_MISMATCH(2),
    TIMEOUT(3),
    PROTOCOL_ERROR(4);

    companion object {
        fun fromWireValue(value: Int): PairingAbortReason? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class PairingAbortPayload(val reason: PairingAbortReason)

object PairingAbortPayloadCodec {
    const val PAYLOAD_SIZE = 4

    fun encode(payload: PairingAbortPayload): ByteArray =
        byteArrayOf(payload.reason.wireValue.toByte(), 0, 0, 0)

    fun decode(bytes: ByteArray): PairingAbortPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "PAIRING_ABORT payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes[1] == 0.toByte() && bytes[2] == 0.toByte() && bytes[3] == 0.toByte()) {
            "PAIRING_ABORT reserved bytes must be zero."
        }

        val reason = PairingAbortReason.fromWireValue(bytes[0].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown pairing abort reason.")

        return PairingAbortPayload(reason)
    }
}

class PairingKeyAgreement : AutoCloseable {
    private val keyPair =
        KeyPairGenerator.getInstance("EC").apply {
            initialize(ECGenParameterSpec(CURVE_NAME))
        }.generateKeyPair()

    val publicKey: ByteArray =
        encodeUncompressedPublicKey(keyPair.public as ECPublicKey)

    fun deriveSharedSecret(remotePublicKey: ByteArray): ByteArray {
        PairingExchangePayloadCodec.validatePublicKey(remotePublicKey)

        val parameters = AlgorithmParameters.getInstance("EC").apply {
            init(ECGenParameterSpec(CURVE_NAME))
        }.getParameterSpec(ECParameterSpec::class.java)

        val x = BigInteger(1, remotePublicKey.copyOfRange(1, 33))
        val y = BigInteger(1, remotePublicKey.copyOfRange(33, 65))
        val spec = ECPublicKeySpec(ECPoint(x, y), parameters)
        val remote = KeyFactory.getInstance("EC").generatePublic(spec)

        return KeyAgreement.getInstance("ECDH").run {
            init(keyPair.private)
            doPhase(remote, true)
            generateSecret()
        }.also {
            require(it.size == PairingCrypto.SHARED_SECRET_SIZE) {
                "Unexpected P-256 shared secret length."
            }
        }
    }

    override fun close() {
        publicKey.fill(0)
    }

    private fun encodeUncompressedPublicKey(publicKey: ECPublicKey): ByteArray {
        val x = unsigned32(publicKey.w.affineX)
        val y = unsigned32(publicKey.w.affineY)

        return ByteArray(65).also { out ->
            out[0] = 0x04
            x.copyInto(out, 1)
            y.copyInto(out, 33)
            x.fill(0)
            y.fill(0)
        }
    }

    private fun unsigned32(value: BigInteger): ByteArray {
        val encoded = value.toByteArray()
        val unsigned =
            if (encoded.size == 33 && encoded[0] == 0.toByte()) {
                encoded.copyOfRange(1, 33)
            } else {
                encoded
            }

        require(unsigned.size <= 32) {
            "Unexpected P-256 coordinate length."
        }

        return ByteArray(32).also { out ->
            unsigned.copyInto(out, 32 - unsigned.size)
        }
    }

    companion object {
        private const val CURVE_NAME = "secp256r1"
    }
}

object PairingCrypto {
    const val SHARED_SECRET_SIZE = 32
    const val TRUST_KEY_SIZE = 32

    private val transcriptLabel = "NATSX-PAIRING-V1".toByteArray(Charsets.US_ASCII)
    private val trustInfo = "NATSX-PAIRING-TRUST-V1".toByteArray(Charsets.US_ASCII)
    private val sasInfo = "NATSX-PAIRING-SAS-V1".toByteArray(Charsets.US_ASCII)
    private val confirmLabel = "NATSX-PAIRING-CONFIRM-V1".toByteArray(Charsets.US_ASCII)

    fun computeTranscriptHash(
        offer: PairingOfferPayload,
        response: PairingResponsePayload,
    ): ByteArray {
        val offerBytes = PairingExchangePayloadCodec.encodeOffer(offer)
        val responseBytes = PairingExchangePayloadCodec.encodeResponse(response)
        val transcript = transcriptLabel + offerBytes + responseBytes
        val hash = MessageDigest.getInstance("SHA-256").digest(transcript)
        transcript.fill(0)
        return hash
    }

    fun deriveTrustKey(
        sharedSecret: ByteArray,
        transcriptHash: ByteArray,
    ): ByteArray {
        validateKdfInputs(sharedSecret, transcriptHash)
        return hkdfSha256(sharedSecret, transcriptHash, trustInfo)
    }

    fun deriveSixDigitCode(
        sharedSecret: ByteArray,
        transcriptHash: ByteArray,
    ): String {
        validateKdfInputs(sharedSecret, transcriptHash)

        val sasKey = hkdfSha256(sharedSecret, transcriptHash, sasInfo)
        val value = ByteBuffer.wrap(sasKey, 0, 4)
            .order(ByteOrder.BIG_ENDIAN)
            .int
            .toUInt()
        sasKey.fill(0)

        return (value % 1_000_000u).toString().padStart(6, '0')
    }

    fun createConfirmationProof(
        trustKey: ByteArray,
        transcriptHash: ByteArray,
        role: PairingRole,
    ): ByteArray {
        require(trustKey.size == TRUST_KEY_SIZE) {
            "Trust key must be exactly $TRUST_KEY_SIZE bytes."
        }
        require(transcriptHash.size == 32) {
            "Pairing transcript hash must be exactly 32 bytes."
        }

        val input =
            confirmLabel +
                transcriptHash +
                byteArrayOf(role.wireValue.toByte())

        val proof = hmacSha256(trustKey, input)
        input.fill(0)
        return proof
    }

    fun verifyConfirmationProof(
        trustKey: ByteArray,
        transcriptHash: ByteArray,
        role: PairingRole,
        suppliedProof: ByteArray,
    ): Boolean {
        if (suppliedProof.size != PairingConfirmPayloadCodec.PROOF_SIZE) return false

        val expected = createConfirmationProof(
            trustKey,
            transcriptHash,
            role,
        )
        val valid = MessageDigest.isEqual(expected, suppliedProof)
        expected.fill(0)
        return valid
    }

    private fun hkdfSha256(
        ikm: ByteArray,
        salt: ByteArray,
        info: ByteArray,
    ): ByteArray {
        val prk = hmacSha256(salt, ikm)
        val expandInput = info + byteArrayOf(0x01)
        val result = hmacSha256(prk, expandInput)

        prk.fill(0)
        expandInput.fill(0)
        return result
    }

    private fun validateKdfInputs(
        sharedSecret: ByteArray,
        transcriptHash: ByteArray,
    ) {
        require(sharedSecret.size == SHARED_SECRET_SIZE) {
            "P-256 shared secret must be exactly $SHARED_SECRET_SIZE bytes."
        }
        require(transcriptHash.size == 32) {
            "Pairing transcript hash must be exactly 32 bytes."
        }
    }

    private fun hmacSha256(key: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
