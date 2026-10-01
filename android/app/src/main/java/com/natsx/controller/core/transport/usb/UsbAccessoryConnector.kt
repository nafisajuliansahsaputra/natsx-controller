package com.natsx.controller.core.transport.usb

import android.app.PendingIntent
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.ParcelFileDescriptor
import android.system.Os
import android.system.OsConstants
import android.system.StructPollfd
import java.io.Closeable
import java.io.IOException
import java.net.SocketTimeoutException
import java.util.concurrent.atomic.AtomicBoolean
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
    private val closed = AtomicBoolean(false)

    fun awaitReadable(
        timeoutMillis: Int,
        stage: String,
    ) {
        require(timeoutMillis >= 0)

        if (closed.get()) {
            throw IOException(
                "USB accessory connection is closed.",
            )
        }

        val pollFd =
            StructPollfd().apply {
                fd = descriptor.fileDescriptor
                events =
                    OsConstants.POLLIN
                        .toShort()
            }

        val ready =
            Os.poll(
                arrayOf(pollFd),
                timeoutMillis,
            )

        if (ready == 0) {
            throw SocketTimeoutException(
                "$stage timed out.",
            )
        }

        val revents =
            pollFd.revents.toInt()

        if (
            revents and
                (
                    OsConstants.POLLERR or
                        OsConstants.POLLHUP or
                        OsConstants.POLLNVAL
                ) != 0
        ) {
            throw IOException(
                "$stage failed because the USB accessory pipe closed.",
            )
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

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
