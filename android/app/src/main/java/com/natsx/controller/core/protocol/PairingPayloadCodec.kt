package com.natsx.controller.core.protocol

data class PairingHelloPayload(
    val androidPeerId: PeerId,
    val capabilities: Int,
    val nonce: ByteArray,
    val publicKey: ByteArray,
)

data class PairingResponsePayload(
    val windowsPeerId: PeerId,
    val capabilities: Int,
    val nonce: ByteArray,
    val publicKey: ByteArray,
    val responseProof: ByteArray,
)

data class PairingConfirmationPayload(
    val peerId: PeerId,
    val proof: ByteArray,
)

object PairingPayloadCodec {
    const val HELLO_PAYLOAD_SIZE = 117
    const val RESPONSE_PAYLOAD_SIZE = 149
    const val CONFIRMATION_PAYLOAD_SIZE = 48

    private const val ALLOWED_CAPABILITIES =
        TransportCapabilities.WIFI or
            TransportCapabilities.BLUETOOTH or
            TransportCapabilities.USB_DIRECT

    fun encodeHello(payload: PairingHelloPayload): ByteArray {
        validateCapabilities(payload.capabilities)
        validateNonce(payload.nonce)
        validatePublicKey(payload.publicKey)

        return ByteArray(HELLO_PAYLOAD_SIZE).also { output ->
            payload.androidPeerId.toByteArray().copyInto(output, 0)
            output[16] = payload.capabilities.toByte()
            payload.nonce.copyInto(output, 20)
            payload.publicKey.copyInto(output, 52)
        }
    }

    fun decodeHello(bytes: ByteArray): PairingHelloPayload {
        require(bytes.size == HELLO_PAYLOAD_SIZE)
        require(bytes.sliceArray(17..19).all { it == 0.toByte() })
        val capabilities = bytes[16].toInt() and 0xff
        validateCapabilities(capabilities)
        val nonce = bytes.copyOfRange(20, 52)
        val publicKey = bytes.copyOfRange(52, 117)
        validateNonce(nonce)
        validatePublicKey(publicKey)
        return PairingHelloPayload(
            PeerId.fromBytes(bytes.copyOfRange(0, 16)),
            capabilities,
            nonce,
            publicKey,
        )
    }

    fun encodeResponse(payload: PairingResponsePayload): ByteArray {
        validateCapabilities(payload.capabilities)
        validateNonce(payload.nonce)
        validatePublicKey(payload.publicKey)
        validateProof(payload.responseProof)

        return ByteArray(RESPONSE_PAYLOAD_SIZE).also { output ->
            payload.windowsPeerId.toByteArray().copyInto(output, 0)
            output[16] = payload.capabilities.toByte()
            payload.nonce.copyInto(output, 20)
            payload.publicKey.copyInto(output, 52)
            payload.responseProof.copyInto(output, 117)
        }
    }

    fun decodeResponse(bytes: ByteArray): PairingResponsePayload {
        require(bytes.size == RESPONSE_PAYLOAD_SIZE)
        require(bytes.sliceArray(17..19).all { it == 0.toByte() })
        val capabilities = bytes[16].toInt() and 0xff
        validateCapabilities(capabilities)
        val nonce = bytes.copyOfRange(20, 52)
        val publicKey = bytes.copyOfRange(52, 117)
        val proof = bytes.copyOfRange(117, 149)
        validateNonce(nonce)
        validatePublicKey(publicKey)
        validateProof(proof)
        return PairingResponsePayload(
            PeerId.fromBytes(bytes.copyOfRange(0, 16)),
            capabilities,
            nonce,
            publicKey,
            proof,
        )
    }

    fun encodeConfirmation(payload: PairingConfirmationPayload): ByteArray {
        validateProof(payload.proof)
        return ByteArray(CONFIRMATION_PAYLOAD_SIZE).also { output ->
            payload.peerId.toByteArray().copyInto(output, 0)
            payload.proof.copyInto(output, 16)
        }
    }

    fun decodeConfirmation(bytes: ByteArray): PairingConfirmationPayload {
        require(bytes.size == CONFIRMATION_PAYLOAD_SIZE)
        return PairingConfirmationPayload(
            PeerId.fromBytes(bytes.copyOfRange(0, 16)),
            bytes.copyOfRange(16, 48),
        )
    }

    private fun validateCapabilities(capabilities: Int) {
        require(capabilities and ALLOWED_CAPABILITIES.inv() == 0)
    }

    private fun validateNonce(nonce: ByteArray) {
        require(nonce.size == PairingCrypto.NONCE_SIZE)
    }

    private fun validatePublicKey(publicKey: ByteArray) {
        require(
            publicKey.size == PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE &&
                publicKey[0] == 0x04.toByte(),
        )
    }

    private fun validateProof(proof: ByteArray) {
        require(proof.size == PairingCrypto.DERIVED_KEY_SIZE)
    }
}
