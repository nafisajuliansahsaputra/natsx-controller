package com.natsx.controller.core.protocol

import java.security.SecureRandom

enum class PairingRole(
    val wireValue: Int,
) {
    ANDROID_CONTROLLER(1),
    WINDOWS_RECEIVER(2);

    companion object {
        fun fromWireValue(value: Int): PairingRole? =
            entries.firstOrNull { it.wireValue == value }
    }
}

enum class PairingAbortReason(
    val wireValue: Int,
) {
    USER_REJECTED(1),
    CODE_MISMATCH(2),
    TIMEOUT(3),
    PROTOCOL_ERROR(4);

    companion object {
        fun fromWireValue(value: Int): PairingAbortReason? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class PairingOfferPayload(
    val androidPeerId: PeerId,
    val androidNonce: ByteArray,
    val androidPublicKey: ByteArray,
)

data class PairingResponsePayload(
    val windowsPeerId: PeerId,
    val windowsNonce: ByteArray,
    val windowsPublicKey: ByteArray,
)

data class PairingConfirmPayload(
    val peerId: PeerId,
    val role: PairingRole,
    val proof: ByteArray,
)

data class PairingAbortPayload(
    val reason: PairingAbortReason,
)

object PairingPayloadCodec {
    const val EXCHANGE_PAYLOAD_SIZE =
        PeerId.SIZE +
            PairingCrypto.NONCE_SIZE +
            PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE

    const val CONFIRMATION_PROOF_SIZE =
        PairingCrypto.DERIVED_KEY_SIZE

    const val CONFIRMATION_PAYLOAD_SIZE =
        PeerId.SIZE + 1 + CONFIRMATION_PROOF_SIZE

    const val ABORT_PAYLOAD_SIZE = 4

    fun createNonce(): ByteArray =
        ByteArray(PairingCrypto.NONCE_SIZE).also {
            SecureRandom().nextBytes(it)
        }

    fun encodeOffer(payload: PairingOfferPayload): ByteArray {
        validateNonce(payload.androidNonce)
        validatePublicKey(payload.androidPublicKey)

        return ByteArray(EXCHANGE_PAYLOAD_SIZE).also { bytes ->
            payload.androidPeerId.toByteArray().copyInto(bytes)
            payload.androidNonce.copyInto(
                bytes,
                destinationOffset = PeerId.SIZE,
            )
            payload.androidPublicKey.copyInto(
                bytes,
                destinationOffset =
                    PeerId.SIZE + PairingCrypto.NONCE_SIZE,
            )
        }
    }

    fun decodeOffer(bytes: ByteArray): PairingOfferPayload {
        require(bytes.size == EXCHANGE_PAYLOAD_SIZE) {
            "PAIRING_OFFER payload must be exactly " +
                EXCHANGE_PAYLOAD_SIZE + " bytes."
        }

        val nonce = bytes.copyOfRange(
            PeerId.SIZE,
            PeerId.SIZE + PairingCrypto.NONCE_SIZE,
        )
        val publicKey = bytes.copyOfRange(
            PeerId.SIZE + PairingCrypto.NONCE_SIZE,
            EXCHANGE_PAYLOAD_SIZE,
        )

        validateNonce(nonce)
        validatePublicKey(publicKey)

        return PairingOfferPayload(
            androidPeerId = PeerId.fromBytes(
                bytes.copyOfRange(0, PeerId.SIZE),
            ),
            androidNonce = nonce,
            androidPublicKey = publicKey,
        )
    }

    fun encodeResponse(payload: PairingResponsePayload): ByteArray {
        validateNonce(payload.windowsNonce)
        validatePublicKey(payload.windowsPublicKey)

        return ByteArray(EXCHANGE_PAYLOAD_SIZE).also { bytes ->
            payload.windowsPeerId.toByteArray().copyInto(bytes)
            payload.windowsNonce.copyInto(
                bytes,
                destinationOffset = PeerId.SIZE,
            )
            payload.windowsPublicKey.copyInto(
                bytes,
                destinationOffset =
                    PeerId.SIZE + PairingCrypto.NONCE_SIZE,
            )
        }
    }

    fun decodeResponse(bytes: ByteArray): PairingResponsePayload {
        require(bytes.size == EXCHANGE_PAYLOAD_SIZE) {
            "PAIRING_RESPONSE payload must be exactly " +
                EXCHANGE_PAYLOAD_SIZE + " bytes."
        }

        val nonce = bytes.copyOfRange(
            PeerId.SIZE,
            PeerId.SIZE + PairingCrypto.NONCE_SIZE,
        )
        val publicKey = bytes.copyOfRange(
            PeerId.SIZE + PairingCrypto.NONCE_SIZE,
            EXCHANGE_PAYLOAD_SIZE,
        )

        validateNonce(nonce)
        validatePublicKey(publicKey)

        return PairingResponsePayload(
            windowsPeerId = PeerId.fromBytes(
                bytes.copyOfRange(0, PeerId.SIZE),
            ),
            windowsNonce = nonce,
            windowsPublicKey = publicKey,
        )
    }

    fun encodeConfirm(payload: PairingConfirmPayload): ByteArray {
        require(payload.proof.size == CONFIRMATION_PROOF_SIZE) {
            "Pairing confirmation proof must be exactly " +
                CONFIRMATION_PROOF_SIZE + " bytes."
        }

        return ByteArray(CONFIRMATION_PAYLOAD_SIZE).also { bytes ->
            payload.peerId.toByteArray().copyInto(bytes)
            bytes[PeerId.SIZE] = payload.role.wireValue.toByte()
            payload.proof.copyInto(
                bytes,
                destinationOffset = PeerId.SIZE + 1,
            )
        }
    }

    fun decodeConfirm(bytes: ByteArray): PairingConfirmPayload {
        require(bytes.size == CONFIRMATION_PAYLOAD_SIZE) {
            "PAIRING_CONFIRM payload must be exactly " +
                CONFIRMATION_PAYLOAD_SIZE + " bytes."
        }

        val role = requireNotNull(
            PairingRole.fromWireValue(
                bytes[PeerId.SIZE].toInt() and 0xFF,
            ),
        ) {
            "Unknown pairing role."
        }

        return PairingConfirmPayload(
            peerId = PeerId.fromBytes(
                bytes.copyOfRange(0, PeerId.SIZE),
            ),
            role = role,
            proof = bytes.copyOfRange(
                PeerId.SIZE + 1,
                CONFIRMATION_PAYLOAD_SIZE,
            ),
        )
    }

    fun encodeAbort(payload: PairingAbortPayload): ByteArray =
        byteArrayOf(
            payload.reason.wireValue.toByte(),
            0,
            0,
            0,
        )

    fun decodeAbort(bytes: ByteArray): PairingAbortPayload {
        require(bytes.size == ABORT_PAYLOAD_SIZE) {
            "PAIRING_ABORT payload must be exactly " +
                ABORT_PAYLOAD_SIZE + " bytes."
        }
        require(
            bytes[1] == 0.toByte() &&
                bytes[2] == 0.toByte() &&
                bytes[3] == 0.toByte(),
        ) {
            "PAIRING_ABORT reserved bytes must be zero."
        }

        return PairingAbortPayload(
            reason = requireNotNull(
                PairingAbortReason.fromWireValue(
                    bytes[0].toInt() and 0xFF,
                ),
            ) {
                "Unknown pairing abort reason."
            },
        )
    }

    private fun validateNonce(nonce: ByteArray) {
        require(nonce.size == PairingCrypto.NONCE_SIZE) {
            "Pairing nonce must be exactly " +
                PairingCrypto.NONCE_SIZE + " bytes."
        }
    }

    private fun validatePublicKey(publicKey: ByteArray) {
        require(
            publicKey.size ==
                PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE &&
                publicKey[0] == 0x04.toByte(),
        ) {
            "Pairing public key must be a 65-byte uncompressed P-256 point."
        }
    }
}

object PairingFrameCodec {
    fun encodeOffer(
        payload: PairingOfferPayload,
        monotonicTimestampMicros: ULong = 0uL,
    ): ByteArray =
        encode(
            MessageType.PAIRING_OFFER,
            PairingPayloadCodec.encodeOffer(payload),
            monotonicTimestampMicros,
        )

    fun decodeOffer(frameBytes: ByteArray): PairingOfferPayload =
        PairingPayloadCodec.decodeOffer(
            decode(frameBytes, MessageType.PAIRING_OFFER),
        )

    fun encodeResponse(
        payload: PairingResponsePayload,
        monotonicTimestampMicros: ULong = 0uL,
    ): ByteArray =
        encode(
            MessageType.PAIRING_RESPONSE,
            PairingPayloadCodec.encodeResponse(payload),
            monotonicTimestampMicros,
        )

    fun decodeResponse(frameBytes: ByteArray): PairingResponsePayload =
        PairingPayloadCodec.decodeResponse(
            decode(frameBytes, MessageType.PAIRING_RESPONSE),
        )

    fun encodeConfirm(
        payload: PairingConfirmPayload,
        monotonicTimestampMicros: ULong = 0uL,
    ): ByteArray =
        encode(
            MessageType.PAIRING_CONFIRM,
            PairingPayloadCodec.encodeConfirm(payload),
            monotonicTimestampMicros,
        )

    fun decodeConfirm(frameBytes: ByteArray): PairingConfirmPayload =
        PairingPayloadCodec.decodeConfirm(
            decode(frameBytes, MessageType.PAIRING_CONFIRM),
        )

    fun encodeAbort(
        payload: PairingAbortPayload,
        monotonicTimestampMicros: ULong = 0uL,
    ): ByteArray =
        encode(
            MessageType.PAIRING_ABORT,
            PairingPayloadCodec.encodeAbort(payload),
            monotonicTimestampMicros,
        )

    fun decodeAbort(frameBytes: ByteArray): PairingAbortPayload =
        PairingPayloadCodec.decodeAbort(
            decode(frameBytes, MessageType.PAIRING_ABORT),
        )

    private fun encode(
        messageType: MessageType,
        payload: ByteArray,
        monotonicTimestampMicros: ULong,
    ): ByteArray =
        try {
            ProtocolFrameCodec.encode(
                ProtocolFrame(
                    version = ProtocolVersion.Current,
                    messageType = messageType,
                    flags = FrameFlags.NONE,
                    sessionId = SessionId.Zero,
                    sequence = 0u,
                    monotonicTimestampMicros = monotonicTimestampMicros,
                    payload = payload,
                ),
            )
        } finally {
            payload.fill(0)
        }

    private fun decode(
        frameBytes: ByteArray,
        expectedType: MessageType,
    ): ByteArray {
        val frame = ProtocolFrameCodec.decode(frameBytes)

        require(
            frame.messageType == expectedType &&
                frame.flags == FrameFlags.NONE &&
                frame.sessionId == SessionId.Zero &&
                frame.sequence == 0u,
        ) {
            "Invalid " + expectedType + " pairing envelope."
        }

        return frame.payload
    }
}
