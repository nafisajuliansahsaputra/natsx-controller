package com.natsx.controller.core.transport.usb

/**
 * Android Open Accessory v1 identities used by NATSX USB Direct.
 *
 * ADB-capable accessory identity (0x2D01) is intentionally excluded from the
 * production contract.
 */
object AndroidOpenAccessory {
    const val GOOGLE_VENDOR_ID = 0x18D1

    const val ACCESSORY_PRODUCT_ID = 0x2D00

    const val GET_PROTOCOL_REQUEST = 51
    const val SEND_STRING_REQUEST = 52
    const val START_ACCESSORY_REQUEST = 53

    const val MANUFACTURER = "NATSX"
    const val MODEL = "NATSX Controller"
    const val DESCRIPTION = "NATSX direct Android controller transport"
    const val VERSION = "1"
}
