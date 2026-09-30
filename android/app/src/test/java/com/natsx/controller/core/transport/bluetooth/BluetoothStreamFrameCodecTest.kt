package com.natsx.controller.core.transport.bluetooth

import java.io.ByteArrayInputStream
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class BluetoothStreamFrameCodecTest {
    private val canonicalFrame =
        hex(
            "4e584331010007012800100000112233445566778899aabbccddeeff" +
                "040302010807060504030201290205000080ff7fc7cf393011fa0000" +
                "200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
        )

    @Test
    fun canonicalFrameGetsLittleEndianLengthPrefix() {
        val encoded =
            BluetoothStreamFrameCodec.encode(canonicalFrame)

        assertEquals(76, canonicalFrame.size)
        assertEquals(
            "4c00" + canonicalFrame.toHex(),
            encoded.toHex(),
        )
    }

    @Test
    fun readFrameRoundTripsFramedMessage() {
        val encoded =
            BluetoothStreamFrameCodec.encode(canonicalFrame)

        val decoded =
            BluetoothStreamFrameCodec.readFrame(
                ByteArrayInputStream(encoded),
            )

        assertArrayEquals(canonicalFrame, decoded)
    }

    @Test(expected = IllegalArgumentException::class)
    fun zeroLengthFrameIsRejected() {
        BluetoothStreamFrameCodec.decodeLengthPrefix(
            byteArrayOf(0, 0),
        )
    }

    @Test(expected = IllegalArgumentException::class)
    fun oversizedFrameIsRejected() {
        BluetoothStreamFrameCodec.decodeLengthPrefix(
            byteArrayOf(0x3D, 0x10),
        )
    }

    private fun hex(value: String): ByteArray {
        require(value.length % 2 == 0)

        return ByteArray(value.length / 2) { index ->
            value
                .substring(index * 2, index * 2 + 2)
                .toInt(16)
                .toByte()
        }
    }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
