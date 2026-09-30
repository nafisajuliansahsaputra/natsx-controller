package com.natsx.controller.core.transport.wifi

import java.nio.ByteBuffer
import java.nio.ByteOrder

data class WifiDiscoveryResponse(
    val realtimePort: Int,
    val receiverId: ByteArray,
    val receiverName: String,
) {
    override fun equals(other: Any?): Boolean =
        other is WifiDiscoveryResponse &&
            realtimePort == other.realtimePort &&
            receiverId.contentEquals(other.receiverId) &&
            receiverName == other.receiverName

    override fun hashCode(): Int {
        var result = realtimePort
        result = 31 * result + receiverId.contentHashCode()
        result = 31 * result + receiverName.hashCode()
        return result
    }
}

object WifiDiscoveryProtocol {
    private val REQUEST_MAGIC = "NXCDISC1".encodeToByteArray()
    private val RESPONSE_MAGIC = "NXCANN01".encodeToByteArray()

    const val DISCOVERY_PORT = 42160
    const val DEFAULT_REALTIME_PORT = 42161
    const val REQUEST_SIZE = 8
    const val FIXED_RESPONSE_SIZE = 29
    const val MAXIMUM_NAME_BYTES = 63

    fun createRequest(): ByteArray = REQUEST_MAGIC.copyOf()

    fun isRequest(payload: ByteArray): Boolean =
        payload.contentEquals(REQUEST_MAGIC)

    fun decodeResponse(payload: ByteArray): WifiDiscoveryResponse {
        require(payload.size >= FIXED_RESPONSE_SIZE) {
            "Discovery response is too short."
        }

        require(payload.copyOfRange(0, 8).contentEquals(RESPONSE_MAGIC)) {
            "Invalid discovery response magic."
        }

        val major = payload[8].toInt() and 0xFF
        require(major == 1) {
            "Unsupported discovery major version $major."
        }

        val nameLength = payload[28].toInt() and 0xFF
        require(nameLength in 1..MAXIMUM_NAME_BYTES) {
            "Invalid discovery receiver name length."
        }

        require(payload.size == FIXED_RESPONSE_SIZE + nameLength) {
            "Discovery response length does not match receiver name length."
        }

        val port =
            ByteBuffer
                .wrap(payload, 10, 2)
                .order(ByteOrder.LITTLE_ENDIAN)
                .short
                .toInt() and 0xFFFF

        val receiverId = payload.copyOfRange(12, 28)
        val receiverName =
            payload
                .copyOfRange(FIXED_RESPONSE_SIZE, payload.size)
                .decodeToString()

        return WifiDiscoveryResponse(
            realtimePort = port,
            receiverId = receiverId,
            receiverName = receiverName,
        )
    }
}
