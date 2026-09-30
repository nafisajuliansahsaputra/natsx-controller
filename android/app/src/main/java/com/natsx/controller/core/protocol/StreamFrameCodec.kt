package com.natsx.controller.core.protocol

import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream

object StreamFrameCodec {
    const val PREFIX_SIZE = 2

    const val MINIMUM_FRAME_SIZE =
        ProtocolConstants.HEADER_SIZE +
            ProtocolConstants.CRC_SIZE

    const val MAXIMUM_FRAME_SIZE =
        ProtocolConstants.HEADER_SIZE +
            ProtocolConstants.MAXIMUM_PAYLOAD_SIZE +
            ProtocolConstants.CRC_SIZE +
            ProtocolConstants.AUTHENTICATION_TAG_SIZE

    @Synchronized
    fun write(
        output: OutputStream,
        frame: ProtocolFrame,
        authenticationKey: ByteArray? = null,
    ) {
        val encoded =
            ProtocolFrameCodec.encode(
                frame,
                authenticationKey,
            )

        require(encoded.size in MINIMUM_FRAME_SIZE..MAXIMUM_FRAME_SIZE)

        output.write(encoded.size and 0xFF)
        output.write((encoded.size ushr 8) and 0xFF)
        output.write(encoded)
        output.flush()
    }

    fun read(
        input: InputStream,
        authenticationKey: ByteArray? = null,
    ): ProtocolFrame {
        val low = input.read()
        if (low < 0) {
            throw EOFException(
                "Stream ended before frame length prefix.",
            )
        }

        val high = input.read()
        if (high < 0) {
            throw EOFException(
                "Stream ended in frame length prefix.",
            )
        }

        val frameLength = low or (high shl 8)

        require(
            frameLength in
                MINIMUM_FRAME_SIZE..MAXIMUM_FRAME_SIZE,
        ) {
            "Invalid stream frame length $frameLength."
        }

        val frameBytes = ByteArray(frameLength)
        readExactly(input, frameBytes)

        return ProtocolFrameCodec.decode(
            frameBytes,
            authenticationKey,
        )
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
                    "Stream ended in the middle of a framed protocol message.",
                )
            }

            offset += read
        }
    }
}
