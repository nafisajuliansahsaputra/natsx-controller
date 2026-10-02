package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class ProtocolTransport(val wireValue: Int) {
    WIFI(1),
    BLUETOOTH(2),
    USB_DIRECT(3);

    companion object {
        fun fromWireValue(value: Int): ProtocolTransport? =
            entries.firstOrNull { it.wireValue == value }
    }
}

enum class TransportPreferenceMode(
    val wireValue: Int,
) {
    AUTO(0),
    WIFI(1),
    BLUETOOTH(2),
    USB_DIRECT(3);

    companion object {
        fun fromWireValue(
            value: Int,
        ): TransportPreferenceMode? =
            entries.firstOrNull {
                it.wireValue == value
            }
    }
}

data class TransportPreferencePayload(
    val mode: TransportPreferenceMode,
)

object TransportPreferencePayloadCodec {
    const val PAYLOAD_SIZE = 4

    fun encode(
        payload: TransportPreferencePayload,
    ): ByteArray =
        byteArrayOf(
            payload.mode.wireValue.toByte(),
            0,
            0,
            0,
        )

    fun decode(
        bytes: ByteArray,
    ): TransportPreferencePayload {
        require(
            bytes.size == PAYLOAD_SIZE,
        ) {
            "TRANSPORT_PREFERENCE payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(
            bytes.sliceArray(1..3)
                .all {
                    it == 0.toByte()
                },
        ) {
            "TRANSPORT_PREFERENCE reserved bytes must be zero."
        }

        val mode =
            TransportPreferenceMode
                .fromWireValue(
                    bytes[0].toInt() and 0xFF,
                )
                ?: throw IllegalArgumentException(
                    "Unknown transport preference mode.",
                )

        return TransportPreferencePayload(
            mode,
        )
    }
}

data class HeartbeatAckPayload(val echoedTimestampMicros: ULong)

object HeartbeatAckPayloadCodec {
    const val PAYLOAD_SIZE = 8

    fun encode(payload: HeartbeatAckPayload): ByteArray =
        ByteBuffer.allocate(PAYLOAD_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .putLong(payload.echoedTimestampMicros.toLong())
            .array()

    fun decode(bytes: ByteArray): HeartbeatAckPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "HEARTBEAT_ACK payload must be exactly $PAYLOAD_SIZE bytes."
        }
        val value = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).long.toULong()
        return HeartbeatAckPayload(value)
    }
}

data class TransportReadyPayload(val transport: ProtocolTransport)

object TransportReadyPayloadCodec {
    const val PAYLOAD_SIZE = 4

    fun encode(payload: TransportReadyPayload): ByteArray =
        byteArrayOf(payload.transport.wireValue.toByte(), 0, 0, 0)

    fun decode(bytes: ByteArray): TransportReadyPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "TRANSPORT_READY payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes.sliceArray(1..3).all { it == 0.toByte() }) {
            "TRANSPORT_READY reserved bytes must be zero."
        }
        val transport = ProtocolTransport.fromWireValue(bytes[0].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown protocol transport.")
        return TransportReadyPayload(transport)
    }
}

data class HandoverPayload(
    val transport: ProtocolTransport,
    val stateSequence: UInt,
)

object HandoverPayloadCodec {
    const val PAYLOAD_SIZE = 8

    fun encode(payload: HandoverPayload): ByteArray =
        ByteBuffer.allocate(PAYLOAD_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(payload.transport.wireValue.toByte())
                put(0)
                put(0)
                put(0)
                putInt(payload.stateSequence.toInt())
            }
            .array()

    fun decode(bytes: ByteArray): HandoverPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "Handover payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes.sliceArray(1..3).all { it == 0.toByte() }) {
            "HANDOVER reserved bytes must be zero."
        }

        val transport = ProtocolTransport.fromWireValue(bytes[0].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown protocol transport.")
        val sequence = ByteBuffer.wrap(bytes, 4, 4)
            .order(ByteOrder.LITTLE_ENDIAN)
            .int
            .toUInt()

        return HandoverPayload(transport, sequence)
    }
}

data class RumblePayload(
    val lowFrequencyMotor: Int,
    val highFrequencyMotor: Int,
)

object RumblePayloadCodec {
    const val PAYLOAD_SIZE = 4

    fun encode(payload: RumblePayload): ByteArray {
        require(payload.lowFrequencyMotor in 0..255)
        require(payload.highFrequencyMotor in 0..255)

        return byteArrayOf(
            payload.lowFrequencyMotor.toByte(),
            payload.highFrequencyMotor.toByte(),
            0,
            0,
        )
    }

    fun decode(bytes: ByteArray): RumblePayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "RUMBLE payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes[2] == 0.toByte() && bytes[3] == 0.toByte()) {
            "RUMBLE reserved bytes must be zero."
        }

        return RumblePayload(
            lowFrequencyMotor = bytes[0].toInt() and 0xFF,
            highFrequencyMotor = bytes[1].toInt() and 0xFF,
        )
    }
}

enum class DisconnectReason(val wireValue: Int) {
    NORMAL(0),
    APP_STOPPING(1),
    TRANSPORT_CLOSING(2),
    PROTOCOL_ERROR(3),
    AUTHENTICATION_FAILED(4);

    companion object {
        fun fromWireValue(value: Int): DisconnectReason? =
            entries.firstOrNull { it.wireValue == value }
    }
}

data class DisconnectPayload(val reason: DisconnectReason)

object DisconnectPayloadCodec {
    const val PAYLOAD_SIZE = 4

    fun encode(payload: DisconnectPayload): ByteArray =
        byteArrayOf(payload.reason.wireValue.toByte(), 0, 0, 0)

    fun decode(bytes: ByteArray): DisconnectPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "DISCONNECT payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes.sliceArray(1..3).all { it == 0.toByte() }) {
            "DISCONNECT reserved bytes must be zero."
        }

        val reason = DisconnectReason.fromWireValue(bytes[0].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown disconnect reason.")
        return DisconnectPayload(reason)
    }
}
