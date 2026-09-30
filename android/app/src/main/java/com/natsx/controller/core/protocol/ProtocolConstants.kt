package com.natsx.controller.core.protocol

object ProtocolConstants {
    const val HEADER_SIZE = 40
    const val CRC_SIZE = 4
    const val AUTHENTICATION_TAG_SIZE = 16
    const val GAMEPAD_STATE_PAYLOAD_SIZE = 16
    const val MAXIMUM_PAYLOAD_SIZE = 4096

    val MAGIC = byteArrayOf(
        'N'.code.toByte(),
        'X'.code.toByte(),
        'C'.code.toByte(),
        '1'.code.toByte(),
    )
}
