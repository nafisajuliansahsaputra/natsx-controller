package com.natsx.controller.core.protocol

import java.security.SecureRandom
import java.nio.ByteBuffer
import java.nio.ByteOrder

class PeerId private constructor(
    private val bytes: ByteArray,
) {
    init {
        require(bytes.size == SIZE)
    }

    fun toByteArray(): ByteArray = bytes.copyOf()

    override fun equals(other: Any?): Boolean =
        other is PeerId && bytes.contentEquals(other.bytes)

    override fun hashCode(): Int = bytes.contentHashCode()

    override fun toString(): String =
        bytes.joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }

    companion object {
        const val SIZE = 16

        val Zero = PeerId(ByteArray(SIZE))

        fun fromBytes(bytes: ByteArray): PeerId {
            require(bytes.size == SIZE)
            return PeerId(bytes.copyOf())
        }

        fun createRandom(): PeerId {
            val bytes = ByteArray(SIZE)
            SecureRandom().nextBytes(bytes)
            return PeerId(bytes)
        }
    }
}

enum class PeerRole(val wireValue: Int) {
    ANDROID_CONTROLLER(1),
    WINDOWS_RECEIVER(2);

    companion object {
        fun fromWireValue(value: Int): PeerRole? =
            entries.firstOrNull { it.wireValue == value }
    }
}

object TransportCapabilities {
    const val NONE = 0
    const val WIFI = 1 shl 0
    const val BLUETOOTH = 1 shl 1
    const val USB_DIRECT = 1 shl 2
    const val ALLOWED_MASK = WIFI or BLUETOOTH or USB_DIRECT
}

data class HelloPayload(
    val role: PeerRole,
    val capabilities: Int,
    val peerId: PeerId,
    val realtimePort: Int,
    val discoveryNonce: UInt,
)

object HelloPayloadCodec {
    const val PAYLOAD_SIZE = 24

    fun encode(payload: HelloPayload): ByteArray {
        validate(payload)

        return ByteBuffer
            .allocate(PAYLOAD_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(payload.role.wireValue.toByte())
                put(payload.capabilities.toByte())
                put(payload.peerId.toByteArray())
                putShort(payload.realtimePort.toShort())
                putInt(payload.discoveryNonce.toInt())
            }
            .array()
    }

    fun decode(bytes: ByteArray): HelloPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "HELLO payload must be exactly $PAYLOAD_SIZE bytes."
        }

        val buffer = ByteBuffer
            .wrap(bytes)
            .order(ByteOrder.LITTLE_ENDIAN)

        val role =
            PeerRole.fromWireValue(buffer.get().toInt() and 0xFF)
                ?: throw IllegalArgumentException("Unknown HELLO peer role.")

        val capabilities = buffer.get().toInt() and 0xFF
        val peerBytes = ByteArray(PeerId.SIZE)
        buffer.get(peerBytes)

        val payload = HelloPayload(
            role = role,
            capabilities = capabilities,
            peerId = PeerId.fromBytes(peerBytes),
            realtimePort = buffer.short.toInt() and 0xFFFF,
            discoveryNonce = buffer.int.toUInt(),
        )

        validate(payload)
        return payload
    }

    private fun validate(payload: HelloPayload) {
        require(payload.capabilities and TransportCapabilities.ALLOWED_MASK.inv() == 0) {
            "HELLO contains unknown transport capability bits."
        }
        require(payload.realtimePort in 0..65535) {
            "HELLO realtime port is outside uint16 range."
        }

        if (
            payload.role == PeerRole.WINDOWS_RECEIVER &&
            payload.capabilities and TransportCapabilities.WIFI != 0
        ) {
            require(payload.realtimePort != 0) {
                "Wi-Fi capable receiver must advertise a realtime UDP port."
            }
        }
    }
}
