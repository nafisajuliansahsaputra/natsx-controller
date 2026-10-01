package com.natsx.controller.core.transport.usb

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class UsbStreamFrameCodecTest {
    private val canonicalFrame =
        (
            "4e584331010007012800100000112233445566778899aabbccddeeff" +
                "040302010807060504030201290205000080ff7fc7cf393011fa0000" +
                "200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be"
        ).hexToBytes()

    @Test
    fun encodeAddsLittleEndianLengthPrefix() {
        val encoded =
            UsbStreamFrameCodec.encode(canonicalFrame)

        assertEquals(0x4c, encoded[0].toInt() and 0xff)
        assertEquals(0x00, encoded[1].toInt() and 0xff)
        assertArrayEquals(
            canonicalFrame,
            encoded.copyOfRange(2, encoded.size),
        )
    }

    private fun String.hexToBytes(): ByteArray =
        chunked(2)
            .map { it.toInt(16).toByte() }
            .toByteArray()
}
