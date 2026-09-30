package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class DeviceRole(val wireValue: Int) {
    ANDROID_CONTROLLER(1),
    WINDOWS_RECEIVER(2);

    companion object {
        fun fromWireValue(value: Int): DeviceRole? = entries.firstOrNull { it.wireValue == value }
    }
}

object TransportMask {
    const val USB = 1 shl 0
    const val WIFI = 1 shl 1
    const val BLUETOOTH = 1 shl 2
    const val ALLOWED = USB or WIFI or BLUETOOTH
}

object CapabilityFlags {
    const val RUMBLE = 1 shl 0
    const val GUIDE = 1 shl 1
    const val COMPETITIVE_240_HZ = 1 shl 2
    const val WARM_STANDBY = 1 shl 3
    const val ALLOWED = RUMBLE or GUIDE or COMPETITIVE_240_HZ or WARM_STANDBY
}

enum class TrustState(val wireValue: Int) {
    UNPAIRED(0),
    PAIRED(1);

    companion object {
        fun fromWireValue(value: Int): TrustState? = entries.firstOrNull { it.wireValue == value }
    }
}

enum class HandoverReason(val wireValue: Int) {
    PREFERRED_TRANSPORT_READY(1),
    ACTIVE_TRANSPORT_DEGRADED(2),
    ACTIVE_TRANSPORT_CRITICAL_OR_LOST(3),
    USER_OVERRIDE(4);

    companion object {
        fun fromWireValue(value: Int): HandoverReason? = entries.firstOrNull { it.wireValue == value }
    }
}

enum class DisconnectReason(val wireValue: Int) {
    NORMAL_SHUTDOWN(1),
    USER_DISCONNECT(2),
    PROTOCOL_FAILURE(3),
    AUTHENTICATION_FAILURE(4),
    TRANSPORT_REPLACED(5),
    FATAL_LOCAL_ERROR(6);

    companion object {
        fun fromWireValue(value: Int): DisconnectReason? = entries.firstOrNull { it.wireValue == value }
    }
}

data class HelloPayload(
    val deviceId: SessionId,
    val role: DeviceRole,
    val transports: Int,
    val capabilities: Int,
    val minimumMajor: Int,
    val maximumMajor: Int,
    val maximumMinor: Int,
    val trustState: TrustState,
)

data class SessionReadyPayload(
    val selectedMajor: Int,
    val selectedMinor: Int,
    val currentTransport: Int,
    val negotiatedCapabilities: Int,
)

data class HeartbeatPayload(val probeId: UInt)

data class HandoverPreparePayload(
    val candidateTransport: Int,
    val reason: HandoverReason,
    val expectedNextSequence: UInt,
)

data class HandoverCommitPayload(
    val authoritativeTransport: Int,
    val firstAuthoritativeSequence: UInt,
)

data class RumblePayload(
    val lowFrequency: Int,
    val highFrequency: Int,
    val durationMilliseconds: Int,
)

object ControlPayloadCodec {
    const val HELLO_SIZE = 32
    const val AUTH_CHALLENGE_SIZE = 32
    const val AUTH_RESPONSE_SIZE = 16
    const val SESSION_READY_SIZE = 8
    const val HEARTBEAT_SIZE = 8
    const val TRANSPORT_READY_SIZE = 4
    const val HANDOVER_SIZE = 8
    const val RUMBLE_SIZE = 4
    const val DISCONNECT_SIZE = 4

    fun encodeHello(payload: HelloPayload): ByteArray {
        require(payload.transports and TransportMask.ALLOWED.inv() == 0)
        require(payload.capabilities and CapabilityFlags.ALLOWED.inv() == 0)

        return ByteBuffer
            .allocate(HELLO_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(payload.deviceId.toByteArray())
                put(payload.role.wireValue.toByte())
                put(payload.transports.toByte())
                putInt(payload.capabilities)
                put(payload.minimumMajor.toByte())
                put(payload.maximumMajor.toByte())
                put(payload.maximumMinor.toByte())
                put(payload.trustState.wireValue.toByte())
                put(ByteArray(6))
            }
            .array()
    }

    fun decodeHello(bytes: ByteArray): HelloPayload {
        requireSize(bytes, HELLO_SIZE)
        requireReservedZero(bytes, 26, 32, "HELLO")

        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val deviceIdBytes = ByteArray(SessionId.SIZE)
        buffer.get(deviceIdBytes)

        val role =
            DeviceRole.fromWireValue(buffer.get().toInt() and 0xFF)
                ?: throw IllegalArgumentException("Unknown device role.")
        val transports = buffer.get().toInt() and 0xFF
        require(transports and TransportMask.ALLOWED.inv() == 0)

        val capabilities = buffer.int
        require(capabilities and CapabilityFlags.ALLOWED.inv() == 0)

        val minimumMajor = buffer.get().toInt() and 0xFF
        val maximumMajor = buffer.get().toInt() and 0xFF
        val maximumMinor = buffer.get().toInt() and 0xFF
        val trustState =
            TrustState.fromWireValue(buffer.get().toInt() and 0xFF)
                ?: throw IllegalArgumentException("Unknown trust state.")

        return HelloPayload(
            SessionId.fromBytes(deviceIdBytes),
            role,
            transports,
            capabilities,
            minimumMajor,
            maximumMajor,
            maximumMinor,
            trustState,
        )
    }

    fun encodeSessionReady(payload: SessionReadyPayload): ByteArray {
        require(payload.negotiatedCapabilities and CapabilityFlags.ALLOWED.inv() == 0)

        return ByteBuffer.allocate(SESSION_READY_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            put(payload.selectedMajor.toByte())
            put(payload.selectedMinor.toByte())
            put(payload.currentTransport.toByte())
            put(0)
            putInt(payload.negotiatedCapabilities)
        }.array()
    }

    fun decodeSessionReady(bytes: ByteArray): SessionReadyPayload {
        requireSize(bytes, SESSION_READY_SIZE)
        require(bytes[3].toInt() == 0)

        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val major = buffer.get().toInt() and 0xFF
        val minor = buffer.get().toInt() and 0xFF
        val transport = buffer.get().toInt() and 0xFF
        buffer.get()
        val capabilities = buffer.int
        require(capabilities and CapabilityFlags.ALLOWED.inv() == 0)

        return SessionReadyPayload(major, minor, transport, capabilities)
    }

    fun encodeHeartbeat(payload: HeartbeatPayload): ByteArray =
        ByteBuffer.allocate(HEARTBEAT_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            putInt(payload.probeId.toInt())
            putInt(0)
        }.array()

    fun decodeHeartbeat(bytes: ByteArray): HeartbeatPayload {
        requireSize(bytes, HEARTBEAT_SIZE)
        requireReservedZero(bytes, 4, 8, "HEARTBEAT")
        return HeartbeatPayload(
            ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).int.toUInt(),
        )
    }

    fun encodeTransportReady(transport: Int): ByteArray =
        byteArrayOf(transport.toByte(), 0, 0, 0)

    fun decodeTransportReady(bytes: ByteArray): Int {
        requireSize(bytes, TRANSPORT_READY_SIZE)
        requireReservedZero(bytes, 1, 4, "TRANSPORT_READY")
        return bytes[0].toInt() and 0xFF
    }

    fun encodeHandoverPrepare(payload: HandoverPreparePayload): ByteArray =
        ByteBuffer.allocate(HANDOVER_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            put(payload.candidateTransport.toByte())
            put(payload.reason.wireValue.toByte())
            putShort(0)
            putInt(payload.expectedNextSequence.toInt())
        }.array()

    fun decodeHandoverPrepare(bytes: ByteArray): HandoverPreparePayload {
        requireSize(bytes, HANDOVER_SIZE)
        requireReservedZero(bytes, 2, 4, "HANDOVER_PREPARE")
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val transport = buffer.get().toInt() and 0xFF
        val reason =
            HandoverReason.fromWireValue(buffer.get().toInt() and 0xFF)
                ?: throw IllegalArgumentException("Unknown handover reason.")
        buffer.short
        return HandoverPreparePayload(transport, reason, buffer.int.toUInt())
    }

    fun encodeHandoverCommit(payload: HandoverCommitPayload): ByteArray =
        ByteBuffer.allocate(HANDOVER_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            put(payload.authoritativeTransport.toByte())
            put(ByteArray(3))
            putInt(payload.firstAuthoritativeSequence.toInt())
        }.array()

    fun decodeHandoverCommit(bytes: ByteArray): HandoverCommitPayload {
        requireSize(bytes, HANDOVER_SIZE)
        requireReservedZero(bytes, 1, 4, "HANDOVER_COMMIT")
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val transport = buffer.get().toInt() and 0xFF
        buffer.position(4)
        return HandoverCommitPayload(transport, buffer.int.toUInt())
    }

    fun encodeRumble(payload: RumblePayload): ByteArray {
        require(payload.lowFrequency in 0..255)
        require(payload.highFrequency in 0..255)
        require(payload.durationMilliseconds in 0..65535)

        return ByteBuffer.allocate(RUMBLE_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            put(payload.lowFrequency.toByte())
            put(payload.highFrequency.toByte())
            putShort(payload.durationMilliseconds.toShort())
        }.array()
    }

    fun decodeRumble(bytes: ByteArray): RumblePayload {
        requireSize(bytes, RUMBLE_SIZE)
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        return RumblePayload(
            buffer.get().toInt() and 0xFF,
            buffer.get().toInt() and 0xFF,
            buffer.short.toInt() and 0xFFFF,
        )
    }

    fun encodeDisconnect(reason: DisconnectReason): ByteArray =
        byteArrayOf(reason.wireValue.toByte(), 0, 0, 0)

    fun decodeDisconnect(bytes: ByteArray): DisconnectReason {
        requireSize(bytes, DISCONNECT_SIZE)
        requireReservedZero(bytes, 1, 4, "DISCONNECT")
        return DisconnectReason.fromWireValue(bytes[0].toInt() and 0xFF)
            ?: throw IllegalArgumentException("Unknown disconnect reason.")
    }

    fun validateAuthChallenge(bytes: ByteArray): ByteArray {
        requireSize(bytes, AUTH_CHALLENGE_SIZE)
        return bytes.copyOf()
    }

    fun validateAuthResponse(bytes: ByteArray): ByteArray {
        requireSize(bytes, AUTH_RESPONSE_SIZE)
        return bytes.copyOf()
    }

    private fun requireSize(bytes: ByteArray, expected: Int) {
        require(bytes.size == expected) {
            "Expected payload length " + expected + ", got " + bytes.size + "."
        }
    }

    private fun requireReservedZero(bytes: ByteArray, from: Int, to: Int, name: String) {
        require((from until to).all { bytes[it].toInt() == 0 }) {
            name + " reserved bytes must be zero."
        }
    }
}
