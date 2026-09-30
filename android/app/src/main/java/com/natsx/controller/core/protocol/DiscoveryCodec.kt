package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class DiscoveryMessageType(val wireValue: Int) {
    REQUEST(1),
    RESPONSE(2),
}

data class DiscoveryRequest(
    val androidDeviceId: SessionId,
)

data class DiscoveryResponse(
    val windowsDeviceId: SessionId,
    val controllerPort: Int,
    val receiverName: String,
)

object DiscoveryCodec {
    const val REQUEST_SIZE = 24
    const val RESPONSE_HEADER_SIZE = 26
    const val MAXIMUM_RECEIVER_NAME_BYTES = 63

    private val magic = byteArrayOf(
        'N'.code.toByte(),
        'X'.code.toByte(),
        'D'.code.toByte(),
        '1'.code.toByte(),
    )

    fun encodeRequest(request: DiscoveryRequest): ByteArray =
        ByteBuffer.allocate(REQUEST_SIZE).order(ByteOrder.LITTLE_ENDIAN).apply {
            put(magic)
            put(ProtocolVersion.Current.major.toByte())
            put(ProtocolVersion.Current.minor.toByte())
            put(DiscoveryMessageType.REQUEST.wireValue.toByte())
            put(0)
            put(request.androidDeviceId.toByteArray())
        }.array()

    fun decodeRequest(bytes: ByteArray): DiscoveryRequest {
        require(bytes.size == REQUEST_SIZE)
        validatePrefix(bytes, DiscoveryMessageType.REQUEST)
        require(bytes[7].toInt() == 0)
        return DiscoveryRequest(
            SessionId.fromBytes(bytes.copyOfRange(8, 24)),
        )
    }

    fun encodeResponse(response: DiscoveryResponse): ByteArray {
        val name = response.receiverName.toByteArray(Charsets.UTF_8)
        require(name.size <= MAXIMUM_RECEIVER_NAME_BYTES)
        require(response.controllerPort in 1..65535)

        return ByteBuffer
            .allocate(RESPONSE_HEADER_SIZE + name.size)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(magic)
                put(ProtocolVersion.Current.major.toByte())
                put(ProtocolVersion.Current.minor.toByte())
                put(DiscoveryMessageType.RESPONSE.wireValue.toByte())
                put(name.size.toByte())
                putShort(response.controllerPort.toShort())
                put(response.windowsDeviceId.toByteArray())
                put(name)
            }
            .array()
    }

    fun decodeResponse(bytes: ByteArray): DiscoveryResponse {
        require(bytes.size >= RESPONSE_HEADER_SIZE)
        validatePrefix(bytes, DiscoveryMessageType.RESPONSE)

        val nameLength = bytes[7].toInt() and 0xFF
        require(nameLength <= MAXIMUM_RECEIVER_NAME_BYTES)
        require(bytes.size == RESPONSE_HEADER_SIZE + nameLength)

        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        buffer.position(8)
        val port = buffer.short.toInt() and 0xFFFF
        require(port != 0)

        val deviceIdBytes = ByteArray(SessionId.SIZE)
        buffer.get(deviceIdBytes)

        val nameBytes = ByteArray(nameLength)
        buffer.get(nameBytes)

        return DiscoveryResponse(
            windowsDeviceId = SessionId.fromBytes(deviceIdBytes),
            controllerPort = port,
            receiverName = nameBytes.toString(Charsets.UTF_8),
        )
    }

    private fun validatePrefix(
        bytes: ByteArray,
        type: DiscoveryMessageType,
    ) {
        require(bytes.size >= 8)
        require(bytes.copyOfRange(0, 4).contentEquals(magic))
        require((bytes[4].toInt() and 0xFF) == ProtocolVersion.Current.major)
        require((bytes[6].toInt() and 0xFF) == type.wireValue)
    }
}
