package com.natsx.controller.core.protocol

data class ProtocolVersion(
    val major: Int,
    val minor: Int,
) {
    companion object {
        val Current = ProtocolVersion(1, 0)
    }
}

enum class MessageType(val wireValue: Int) {
    HELLO(1),
    AUTH_CHALLENGE(2),
    AUTH_RESPONSE(3),
    SESSION_READY(4),
    HEARTBEAT(5),
    HEARTBEAT_ACK(6),
    GAMEPAD_STATE(7),
    TRANSPORT_READY(8),
    HANDOVER_PREPARE(9),
    HANDOVER_COMMIT(10),
    RUMBLE(11),
    DISCONNECT(12);

    companion object {
        fun fromWireValue(value: Int): MessageType? =
            entries.firstOrNull { it.wireValue == value }
    }
}

object FrameFlags {
    const val NONE = 0
    const val AUTHENTICATED = 1 shl 0
    const val ALLOWED_MASK = AUTHENTICATED
}

class SessionId private constructor(
    private val bytes: ByteArray,
) {
    init {
        require(bytes.size == SIZE)
    }

    fun toByteArray(): ByteArray = bytes.copyOf()

    override fun equals(other: Any?): Boolean =
        other is SessionId && bytes.contentEquals(other.bytes)

    override fun hashCode(): Int = bytes.contentHashCode()

    override fun toString(): String = bytes.joinToString(separator = "") {
        "%02x".format(it.toInt() and 0xFF)
    }

    companion object {
        const val SIZE = 16

        val Zero = SessionId(ByteArray(SIZE))

        fun fromBytes(bytes: ByteArray): SessionId {
            require(bytes.size == SIZE)
            return SessionId(bytes.copyOf())
        }
    }
}

data class ProtocolFrame(
    val version: ProtocolVersion,
    val messageType: MessageType,
    val flags: Int,
    val sessionId: SessionId,
    val sequence: UInt,
    val monotonicTimestampMicros: ULong,
    val payload: ByteArray,
)
