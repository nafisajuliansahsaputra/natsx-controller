package com.natsx.controller.core.protocol

object Crc32C {
    private const val POLYNOMIAL = 0x82F63B78.toInt()

    fun compute(
        data: ByteArray,
        offset: Int = 0,
        length: Int = data.size - offset,
    ): Int {
        require(offset >= 0)
        require(length >= 0)
        require(offset + length <= data.size)

        var crc = -1

        for (index in offset until offset + length) {
            crc = crc xor (data[index].toInt() and 0xFF)

            repeat(8) {
                val mask = -(crc and 1)
                crc = (crc ushr 1) xor (POLYNOMIAL and mask)
            }
        }

        return crc.inv()
    }
}
