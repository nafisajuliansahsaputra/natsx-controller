package com.natsx.controller.core.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object ProtocolFrameCodec {
    fun encode(
        frame: ProtocolFrame,
        authenticationKey: ByteArray? = null,
    ): ByteArray {
        validateForEncode(frame, authenticationKey)

        val authenticated = frame.flags and FrameFlags.AUTHENTICATED != 0
        val trailerSize =
            ProtocolConstants.CRC_SIZE +
                if (authenticated) ProtocolConstants.AUTHENTICATION_TAG_SIZE else 0

        val output = ByteArray(
            ProtocolConstants.HEADER_SIZE +
                frame.payload.size +
                trailerSize,
        )

        val buffer = ByteBuffer.wrap(output).order(ByteOrder.LITTLE_ENDIAN)
        buffer.put(ProtocolConstants.MAGIC)
        buffer.put(frame.version.major.toByte())
        buffer.put(frame.version.minor.toByte())
        buffer.put(frame.messageType.wireValue.toByte())
        buffer.put(frame.flags.toByte())
        buffer.putShort(ProtocolConstants.HEADER_SIZE.toShort())
        buffer.putShort(frame.payload.size.toShort())
        buffer.put(frame.sessionId.toByteArray())
        buffer.putInt(frame.sequence.toInt())
        buffer.putLong(frame.monotonicTimestampMicros.toLong())
        buffer.put(frame.payload)

        val crcOffset = ProtocolConstants.HEADER_SIZE + frame.payload.size
        val crc = Crc32C.compute(output, 0, crcOffset)
        buffer.position(crcOffset)
        buffer.putInt(crc)

        if (authenticated) {
            val key = requireNotNull(authenticationKey)
            val authenticatedLength = crcOffset + ProtocolConstants.CRC_SIZE
            val tag = hmacSha256(
                key = key,
                data = output.copyOfRange(0, authenticatedLength),
            )

            tag.copyInto(
                destination = output,
                destinationOffset = authenticatedLength,
                startIndex = 0,
                endIndex = ProtocolConstants.AUTHENTICATION_TAG_SIZE,
            )

            tag.fill(0)
        }

        return output
    }

    fun decode(
        frameBytes: ByteArray,
        authenticationKey: ByteArray? = null,
    ): ProtocolFrame {
        require(frameBytes.size >= ProtocolConstants.HEADER_SIZE + ProtocolConstants.CRC_SIZE) {
            "Frame is shorter than the minimum v1 frame."
        }

        require(
            frameBytes.copyOfRange(0, 4).contentEquals(ProtocolConstants.MAGIC),
        ) {
            "Invalid protocol magic."
        }

        val buffer = ByteBuffer.wrap(frameBytes).order(ByteOrder.LITTLE_ENDIAN)
        buffer.position(4)

        val version = ProtocolVersion(
            major = buffer.get().toInt() and 0xFF,
            minor = buffer.get().toInt() and 0xFF,
        )

        require(version.major == ProtocolVersion.Current.major) {
            "Unsupported protocol major version ${version.major}."
        }

        val messageType =
            MessageType.fromWireValue(buffer.get().toInt() and 0xFF)
                ?: throw IllegalArgumentException("Unknown v1 message type.")

        val flags = buffer.get().toInt() and 0xFF
        require(flags and FrameFlags.ALLOWED_MASK.inv() == 0) {
            "Unknown v1 frame flags."
        }

        val headerLength = buffer.short.toInt() and 0xFFFF
        require(headerLength == ProtocolConstants.HEADER_SIZE) {
            "Invalid v1 header length."
        }

        val payloadLength = buffer.short.toInt() and 0xFFFF
        require(payloadLength <= ProtocolConstants.MAXIMUM_PAYLOAD_SIZE) {
            "Payload exceeds the v1 maximum."
        }

        val authenticated = flags and FrameFlags.AUTHENTICATED != 0
        val expectedLength =
            ProtocolConstants.HEADER_SIZE +
                payloadLength +
                ProtocolConstants.CRC_SIZE +
                if (authenticated) ProtocolConstants.AUTHENTICATION_TAG_SIZE else 0

        require(frameBytes.size == expectedLength) {
            "Frame length does not match header/trailer fields."
        }

        val sessionBytes = ByteArray(SessionId.SIZE)
        buffer.get(sessionBytes)

        val sequence = buffer.int.toUInt()
        val timestamp = buffer.long.toULong()

        val crcOffset = ProtocolConstants.HEADER_SIZE + payloadLength
        val suppliedCrc =
            ByteBuffer
                .wrap(frameBytes, crcOffset, ProtocolConstants.CRC_SIZE)
                .order(ByteOrder.LITTLE_ENDIAN)
                .int

        val computedCrc = Crc32C.compute(frameBytes, 0, crcOffset)
        require(suppliedCrc == computedCrc) {
            "CRC32C validation failed."
        }

        if (authenticated) {
            val key = requireNotNull(authenticationKey) {
                "Authenticated frame requires a session key."
            }

            val authenticatedLength = crcOffset + ProtocolConstants.CRC_SIZE
            val expectedTag = hmacSha256(
                key = key,
                data = frameBytes.copyOfRange(0, authenticatedLength),
            ).copyOf(ProtocolConstants.AUTHENTICATION_TAG_SIZE)

            val suppliedTag =
                frameBytes.copyOfRange(
                    authenticatedLength,
                    authenticatedLength + ProtocolConstants.AUTHENTICATION_TAG_SIZE,
                )

            val valid = MessageDigest.isEqual(expectedTag, suppliedTag)
            expectedTag.fill(0)
            suppliedTag.fill(0)

            require(valid) {
                "Frame authentication failed."
            }
        }

        val payload =
            frameBytes.copyOfRange(
                ProtocolConstants.HEADER_SIZE,
                ProtocolConstants.HEADER_SIZE + payloadLength,
            )

        return ProtocolFrame(
            version = version,
            messageType = messageType,
            flags = flags,
            sessionId = SessionId.fromBytes(sessionBytes),
            sequence = sequence,
            monotonicTimestampMicros = timestamp,
            payload = payload,
        )
    }

    private fun validateForEncode(
        frame: ProtocolFrame,
        authenticationKey: ByteArray?,
    ) {
        require(frame.version.major == ProtocolVersion.Current.major) {
            "Unsupported protocol major version."
        }

        require(frame.flags and FrameFlags.ALLOWED_MASK.inv() == 0) {
            "Unknown v1 frame flags."
        }

        require(frame.payload.size <= ProtocolConstants.MAXIMUM_PAYLOAD_SIZE) {
            "Payload exceeds the v1 maximum."
        }

        if (frame.flags and FrameFlags.AUTHENTICATED != 0) {
            require(!authenticationKey.isNullOrEmpty()) {
                "Authenticated frame requires a session key."
            }
        }
    }

    private fun hmacSha256(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
