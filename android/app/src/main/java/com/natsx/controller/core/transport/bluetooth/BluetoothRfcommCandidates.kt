package com.natsx.controller.core.transport.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.Context
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID

object BluetoothRfcommConstants {
    val SERVICE_UUID: UUID =
        UUID.fromString(
            "65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11",
        )
}

interface BluetoothRfcommConnection : Closeable {
    val inputStream: InputStream
    val outputStream: OutputStream
}

data class BluetoothRfcommCandidate(
    val routeId: String,
    val connect: () -> BluetoothRfcommConnection,
)

fun interface BluetoothRfcommCandidateProvider {
    fun candidates(): List<BluetoothRfcommCandidate>
}

class BondedBluetoothRfcommCandidateProvider(
    private val context: Context,
    private val permissionGate:
        BluetoothPermissionGate =
        BluetoothPermissionGate(context),
) : BluetoothRfcommCandidateProvider {
    @SuppressLint("MissingPermission")
    override fun candidates():
        List<BluetoothRfcommCandidate> {
        if (
            !permissionGate
                .hasRequiredRuntimePermissions() ||
            !permissionGate.isBluetoothEnabled()
        ) {
            return emptyList()
        }

        val adapter =
            context
                .getSystemService(
                    BluetoothManager::class.java,
                )
                ?.adapter
                ?: return emptyList()

        return adapter
            .bondedDevices
            .sortedBy { device ->
                device.address
            }
            .map { device ->
                BluetoothRfcommCandidate(
                    routeId =
                        device.address,
                    connect = {
                        val socket =
                            device
                                .createRfcommSocketToServiceRecord(
                                    BluetoothRfcommConstants
                                        .SERVICE_UUID,
                                )

                        try {
                            socket.connect()
                            AndroidBluetoothRfcommConnection(
                                socket,
                            )
                        } catch (
                            exception: Exception
                        ) {
                            runCatching {
                                socket.close()
                            }
                            throw exception
                        }
                    },
                )
            }
    }
}

private class AndroidBluetoothRfcommConnection(
    private val socket: BluetoothSocket,
) : BluetoothRfcommConnection {
    override val inputStream:
        InputStream
        get() =
            socket.inputStream

    override val outputStream:
        OutputStream
        get() =
            socket.outputStream

    override fun close() {
        runCatching {
            socket.close()
        }
    }
}
