package com.natsx.controller.core.transport.usb

import android.app.PendingIntent
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.ParcelFileDescriptor
import java.io.Closeable
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream

object UsbAccessoryIdentity {
    const val MANUFACTURER = "NATSX"
    const val MODEL = "NATSX Controller Windows Receiver"

    fun matches(accessory: UsbAccessory): Boolean =
        accessory.manufacturer == MANUFACTURER &&
            accessory.model == MODEL
}

class UsbAccessoryConnection private constructor(
    val accessory: UsbAccessory,
    private val descriptor: ParcelFileDescriptor,
    val input: InputStream,
    val output: OutputStream,
) : Closeable {
    override fun close() {
        try {
            input.close()
        } finally {
            try {
                output.close()
            } finally {
                descriptor.close()
            }
        }
    }

    companion object {
        fun open(
            usbManager: UsbManager,
            accessory: UsbAccessory,
        ): UsbAccessoryConnection {
            require(UsbAccessoryIdentity.matches(accessory)) {
                "USB accessory does not match the NATSX receiver identity."
            }

            check(usbManager.hasPermission(accessory)) {
                "USB accessory permission has not been granted."
            }

            val descriptor =
                checkNotNull(usbManager.openAccessory(accessory)) {
                    "Android could not open the USB accessory."
                }

            return try {
                UsbAccessoryConnection(
                    accessory = accessory,
                    descriptor = descriptor,
                    input = FileInputStream(descriptor.fileDescriptor),
                    output = FileOutputStream(descriptor.fileDescriptor),
                )
            } catch (exception: Exception) {
                descriptor.close()
                throw exception
            }
        }
    }
}

object UsbAccessoryConnector {
    fun findNatsxAccessory(
        usbManager: UsbManager,
    ): UsbAccessory? =
        usbManager.accessoryList
            ?.firstOrNull(UsbAccessoryIdentity::matches)

    fun hasPermission(
        usbManager: UsbManager,
        accessory: UsbAccessory,
    ): Boolean =
        usbManager.hasPermission(accessory)

    fun requestPermission(
        usbManager: UsbManager,
        accessory: UsbAccessory,
        permissionIntent: PendingIntent,
    ) {
        usbManager.requestPermission(
            accessory,
            permissionIntent,
        )
    }
}
