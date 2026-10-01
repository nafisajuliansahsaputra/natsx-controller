package com.natsx.controller.core.transport.usb

import android.app.PendingIntent
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.ParcelFileDescriptor
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream

interface UsbAccessoryConnection : Closeable {
    val inputStream: InputStream
    val outputStream: OutputStream
}

fun interface UsbAccessoryConnectionProvider {
    fun open(): UsbAccessoryConnection
}

class AndroidUsbAccessoryConnection(
    private val fileDescriptor: ParcelFileDescriptor,
) : UsbAccessoryConnection {
    override val inputStream: InputStream =
        FileInputStream(
            fileDescriptor.fileDescriptor,
        )

    override val outputStream: OutputStream =
        FileOutputStream(
            fileDescriptor.fileDescriptor,
        )

    override fun close() {
        runCatching {
            inputStream.close()
        }
        runCatching {
            outputStream.close()
        }
        runCatching {
            fileDescriptor.close()
        }
    }
}

class AndroidUsbAccessoryProvider(
    private val usbManager: UsbManager,
) {
    fun connectedAccessories(): List<UsbAccessory> =
        usbManager.accessoryList
            ?.toList()
            .orEmpty()

    fun hasPermission(
        accessory: UsbAccessory,
    ): Boolean =
        usbManager.hasPermission(accessory)

    fun requestPermission(
        accessory: UsbAccessory,
        pendingIntent: PendingIntent,
    ) {
        usbManager.requestPermission(
            accessory,
            pendingIntent,
        )
    }

    fun connectionProvider(
        accessory: UsbAccessory,
    ): UsbAccessoryConnectionProvider =
        UsbAccessoryConnectionProvider {
            check(
                usbManager.hasPermission(
                    accessory,
                ),
            ) {
                "USB accessory permission has not been granted."
            }

            val descriptor =
                checkNotNull(
                    usbManager.openAccessory(
                        accessory,
                    ),
                ) {
                    "Failed to open Android USB accessory."
                }

            AndroidUsbAccessoryConnection(
                descriptor,
            )
        }
}

interface UsbRealtimeLink :
    RealtimeStateSink,
    Closeable {
    val lastHeartbeatReceivedNanos: Long
}

class UsbAccessoryRealtimeLink internal constructor(
    private val connection: UsbAccessoryConnection,
    private val session: UsbTrustedSession,
    private val sender: UsbRealtimeSender,
) : UsbRealtimeLink {
    private var closed = false

    override val lastHeartbeatReceivedNanos: Long
        get() = sender.lastHeartbeatReceivedNanos

    override fun publish(
        envelope: RealtimeStateEnvelope,
    ) {
        check(!closed) {
            "USB accessory realtime link is closed."
        }

        sender.publish(envelope)
    }

    override fun close() {
        if (closed) {
            return
        }

        closed = true

        try {
            sender.close()
        } finally {
            try {
                session.close()
            } finally {
                connection.close()
            }
        }
    }
}

class UsbAccessoryRealtimeConnector(
    private val connectionProvider:
        UsbAccessoryConnectionProvider,
    private val joinClient:
        UsbSecondarySessionJoinClient,
) {
    fun connect(): UsbRealtimeLink {
        val connection =
            connectionProvider.open()

        try {
            val session =
                joinClient.join(
                    connection.inputStream,
                    connection.outputStream,
                )

            try {
                val sender =
                    UsbRealtimeSender(
                        inputStream =
                            connection.inputStream,
                        outputStream =
                            connection.outputStream,
                        trustedSession =
                            session,
                    )

                return UsbAccessoryRealtimeLink(
                    connection = connection,
                    session = session,
                    sender = sender,
                )
            } catch (exception: Exception) {
                session.close()
                throw exception
            }
        } catch (exception: Exception) {
            runCatching {
                connection.close()
            }
            throw exception
        }
    }
}
