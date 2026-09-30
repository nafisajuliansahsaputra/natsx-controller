package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.ProtocolConstants
import java.io.EOFException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

object BluetoothStreamFrameCodec {
    const val LENGTH_PREFIX_SIZE = 2

    const val MINIMUM_FRAME_SIZE =
        ProtocolConstants.HEADER_SIZE +
            ProtocolConstants.CRC_SIZE

    const val MAXIMUM_FRAME_SIZE =
        ProtocolConstants.HEADER_SIZE +
            ProtocolConstants.MAXIMUM_PAYLOAD_SIZE +
            ProtocolConstants.CRC_SIZE +
            ProtocolConstants.AUTHENTICATION_TAG_SIZE

    fun encode(frame: ByteArray): ByteArray {
        validateFrameLength(frame.size)

        return ByteBuffer
            .allocate(LENGTH_PREFIX_SIZE + frame.size)
            .order(ByteOrder.LITTLE_ENDIAN)
            .putShort(frame.size.toShort())
            .put(frame)
            .array()
    }

    fun decodeLengthPrefix(prefix: ByteArray): Int {
        require(prefix.size == LENGTH_PREFIX_SIZE) {
            "RFCOMM length prefix must be exactly " +
                "$LENGTH_PREFIX_SIZE bytes."
        }

        val length =
            ByteBuffer
                .wrap(prefix)
                .order(ByteOrder.LITTLE_ENDIAN)
                .short
                .toInt() and 0xFFFF

        validateFrameLength(length)
        return length
    }

    fun readFrame(input: InputStream): ByteArray {
        val prefix = ByteArray(LENGTH_PREFIX_SIZE)
        readExactly(input, prefix)

        val frame = ByteArray(
            decodeLengthPrefix(prefix),
        )

        readExactly(input, frame)
        return frame
    }

    private fun readExactly(
        input: InputStream,
        destination: ByteArray,
    ) {
        var offset = 0

        while (offset < destination.size) {
            val read =
                input.read(
                    destination,
                    offset,
                    destination.size - offset,
                )

            if (read < 0) {
                throw EOFException(
                    "RFCOMM stream ended before a complete frame was available.",
                )
            }

            if (read == 0) {
                continue
            }

            offset += read
        }
    }

    private fun validateFrameLength(length: Int) {
        require(
            length in MINIMUM_FRAME_SIZE..MAXIMUM_FRAME_SIZE,
        ) {
            "RFCOMM frame length $length is outside the allowed " +
                "$MINIMUM_FRAME_SIZE..$MAXIMUM_FRAME_SIZE byte range."
        }
    }
}
