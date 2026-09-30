package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class PairingMessageType(val wireValue: Int) {
    PAIR_REQUEST(1),
    PAIR_RESPONSE(2),
    PAIR_CONFIRM(3),
    PAIR_COMPLETE(4),
    PAIR_CANCEL(5);

    companion object {
        fun fromWireValue(value: Int): PairingMessageType? =
            entries.firstOrNull { it.wireValue == value }
    }
}

enum class PairingRole(val wireValue: Int) {
    ANDROID(1),
    WINDOWS(2);

    companion object {
        fun fromWireValue(value: Int): PairingRole? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class PairingHelloPayload(
    val deviceId: SessionId,
    val publicKeyDer: ByteArray,
    val displayName: String,
)

data class PairingConfirmPayload(
    val role: PairingRole,
    val tag: ByteArray,
)

object PairingCodec {
    const val HEADER_SIZE = 8
    const val HELLO_FIXED_SIZE = 28
    const val CONFIRM_SIZE = 28
    const val COMPLETE_SIZE = 24
    const val CANCEL_SIZE = 12
    const val MAXIMUM_PAYLOAD_SIZE = 2048
    const val MAXIMUM_PUBLIC_KEY_SIZE = 512
    const val MAXIMUM_DISPLAY_NAME_BYTES = 63
    const val CONFIRMATION_TAG_SIZE = 16

    private val magic = byteArrayOf(
        'N'.code.toByte(),
        'X'.code.toByte(),
        'P'.code.toByte(),
        '1'.code.toByte(),
    )

    fun encodeRequest(payload: PairingHelloPayload): ByteArray =
        encodeHello(PairingMessageType.PAIR_REQUEST, payload)

    fun encodeResponse(payload: PairingHelloPayload): ByteArray =
        encodeHello(PairingMessageType.PAIR_RESPONSE, payload)

    fun decodeRequest(bytes: ByteArray): PairingHelloPayload =
        decodeHello(PairingMessageType.PAIR_REQUEST, bytes)

    fun decodeResponse(bytes: ByteArray): PairingHelloPayload =
        decodeHello(PairingMessageType.PAIR_RESPONSE, bytes)

    fun encodeConfirm(payload: PairingConfirmPayload): ByteArray {
        require(payload.tag.size == CONFIRMATION_TAG_SIZE)

        return ByteArray(CONFIRM_SIZE).also { bytes ->
            writeHeader(bytes, PairingMessageType.PAIR_CONFIRM)
            bytes[8] = payload.role.wireValue.toByte()
            payload.tag.copyInto(bytes, 12)
        }
    }

    fun decodeConfirm(bytes: ByteArray): PairingConfirmPayload {
        validateExact(
            bytes,
            CONFIRM_SIZE,
            PairingMessageType.PAIR_CONFIRM,
        )
        require(bytes[9].toInt() == 0)
        require(bytes[10].toInt() == 0)
        require(bytes[11].toInt() == 0)

        val role =
            PairingRole.fromWireValue(bytes[8].toInt() and 0xFF)
                ?: throw IllegalArgumentException(
                    "PAIR_CONFIRM role is invalid.",
                )

        return PairingConfirmPayload(
            role = role,
            tag = bytes.copyOfRange(12, 28),
        )
    }

    fun encodeComplete(tag: ByteArray): ByteArray {
        require(tag.size == CONFIRMATION_TAG_SIZE)

        return ByteArray(COMPLETE_SIZE).also { bytes ->
            writeHeader(bytes, PairingMessageType.PAIR_COMPLETE)
            tag.copyInto(bytes, 8)
        }
    }

    fun decodeComplete(bytes: ByteArray): ByteArray {
        validateExact(
            bytes,
            COMPLETE_SIZE,
            PairingMessageType.PAIR_COMPLETE,
        )
        return bytes.copyOfRange(8, 24)
    }

    fun encodeCancel(reason: Int): ByteArray {
        require(reason in 0..255)

        return ByteArray(CANCEL_SIZE).also { bytes ->
            writeHeader(bytes, PairingMessageType.PAIR_CANCEL)
            bytes[8] = reason.toByte()
        }
    }

    fun decodeCancel(bytes: ByteArray): Int {
        validateExact(
            bytes,
            CANCEL_SIZE,
            PairingMessageType.PAIR_CANCEL,
        )
        require(bytes[9].toInt() == 0)
        require(bytes[10].toInt() == 0)
        require(bytes[11].toInt() == 0)
        return bytes[8].toInt() and 0xFF
    }

    fun peekType(bytes: ByteArray): PairingMessageType {
        validateHeader(bytes)
        return PairingMessageType.fromWireValue(
            bytes[6].toInt() and 0xFF,
        ) ?: throw IllegalArgumentException(
            "Unknown pairing message type.",
        )
    }

    private fun encodeHello(
        type: PairingMessageType,
        payload: PairingHelloPayload,
    ): ByteArray {
        require(payload.deviceId != SessionId.Zero)
        require(
            payload.publicKeyDer.size in
                1..MAXIMUM_PUBLIC_KEY_SIZE,
        )

        val nameBytes = payload.displayName.toByteArray(Charsets.UTF_8)
        require(nameBytes.size <= MAXIMUM_DISPLAY_NAME_BYTES)

        val total =
            HELLO_FIXED_SIZE +
                payload.publicKeyDer.size +
                nameBytes.size

        require(total <= MAXIMUM_PAYLOAD_SIZE)

        return ByteBuffer
            .allocate(total)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(magic)
                put(ProtocolVersion.Current.major.toByte())
                put(ProtocolVersion.Current.minor.toByte())
                put(type.wireValue.toByte())
                put(0)
                put(payload.deviceId.toByteArray())
                putShort(payload.publicKeyDer.size.toShort())
                put(nameBytes.size.toByte())
                put(0)
                put(payload.publicKeyDer)
                put(nameBytes)
            }
            .array()
    }

    private fun decodeHello(
        type: PairingMessageType,
        bytes: ByteArray,
    ): PairingHelloPayload {
        require(
            bytes.size in HELLO_FIXED_SIZE..MAXIMUM_PAYLOAD_SIZE,
        )
        validateHeader(bytes)
        require(
            (bytes[6].toInt() and 0xFF) == type.wireValue,
        )
        require(bytes[27].toInt() == 0)

        val deviceId = SessionId.fromBytes(
            bytes.copyOfRange(8, 24),
        )
        require(deviceId != SessionId.Zero)

        val buffer =
            ByteBuffer.wrap(bytes)
                .order(ByteOrder.LITTLE_ENDIAN)

        buffer.position(24)
        val publicKeyLength = buffer.short.toInt() and 0xFFFF
        val nameLength = buffer.get().toInt() and 0xFF
        buffer.get()

        require(
            publicKeyLength in 1..MAXIMUM_PUBLIC_KEY_SIZE,
        )
        require(nameLength <= MAXIMUM_DISPLAY_NAME_BYTES)

        val expected =
            HELLO_FIXED_SIZE + publicKeyLength + nameLength

        require(bytes.size == expected)

        val publicKey = ByteArray(publicKeyLength)
        buffer.get(publicKey)

        val nameBytes = ByteArray(nameLength)
        buffer.get(nameBytes)

        return PairingHelloPayload(
            deviceId = deviceId,
            publicKeyDer = publicKey,
            displayName = nameBytes.toString(Charsets.UTF_8),
        )
    }

    private fun writeHeader(
        bytes: ByteArray,
        type: PairingMessageType,
    ) {
        magic.copyInto(bytes, 0)
        bytes[4] = ProtocolVersion.Current.major.toByte()
        bytes[5] = ProtocolVersion.Current.minor.toByte()
        bytes[6] = type.wireValue.toByte()
        bytes[7] = 0
    }

    private fun validateExact(
        bytes: ByteArray,
        expectedLength: Int,
        type: PairingMessageType,
    ) {
        require(bytes.size == expectedLength)
        validateHeader(bytes)
        require(
            (bytes[6].toInt() and 0xFF) == type.wireValue,
        )
    }

    private fun validateHeader(bytes: ByteArray) {
        require(bytes.size >= HEADER_SIZE)
        require(
            bytes.copyOfRange(0, 4)
                .contentEquals(magic),
        )
        require(
            (bytes[4].toInt() and 0xFF) ==
                ProtocolVersion.Current.major,
        )
        require(bytes[7].toInt() == 0)
    }
}
