package com.natsx.controller.core.protocol

data class SessionReadyPayload(
    val role: PeerRole,
    val capabilities: Int,
    val peerId: PeerId,
)

object SessionReadyPayloadCodec {
    const val PAYLOAD_SIZE = 20

    fun encode(payload: SessionReadyPayload): ByteArray {
        validate(payload)

        return ByteArray(PAYLOAD_SIZE).also { bytes ->
            bytes[0] = payload.role.wireValue.toByte()
            bytes[1] = payload.capabilities.toByte()
            bytes[2] = 0
            bytes[3] = 0
            payload.peerId.toByteArray().copyInto(
                destination = bytes,
                destinationOffset = 4,
            )
        }
    }

    fun decode(bytes: ByteArray): SessionReadyPayload {
        require(bytes.size == PAYLOAD_SIZE) {
            "SESSION_READY payload must be exactly $PAYLOAD_SIZE bytes."
        }
        require(bytes[2].toInt() == 0 && bytes[3].toInt() == 0) {
            "SESSION_READY reserved bytes must be zero."
        }

        val role =
            PeerRole.fromWireValue(bytes[0].toInt() and 0xFF)
                ?: throw IllegalArgumentException(
                    "SESSION_READY contains an unknown peer role.",
                )

        val payload = SessionReadyPayload(
            role = role,
            capabilities = bytes[1].toInt() and 0xFF,
            peerId = PeerId.fromBytes(bytes.copyOfRange(4, 20)),
        )

        validate(payload)
        return payload
    }

    private fun validate(payload: SessionReadyPayload) {
        require(
            payload.capabilities and
                TransportCapabilities.ALLOWED_MASK.inv() == 0,
        ) {
            "SESSION_READY contains unknown transport capability bits."
        }
    }
}
