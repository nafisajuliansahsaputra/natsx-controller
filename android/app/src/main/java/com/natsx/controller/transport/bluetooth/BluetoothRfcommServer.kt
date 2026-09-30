package com.natsx.controller.transport.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import java.io.IOException
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

class BluetoothRfcommServer(
    private val adapter: BluetoothAdapter,
    private val onConnection: (BluetoothSocket) -> Unit,
    private val onError: (Throwable) -> Unit = {},
) : AutoCloseable {
    private val running = AtomicBoolean(false)

    @Volatile
    private var serverSocket: BluetoothServerSocket? = null

    @Volatile
    private var worker: Thread? = null

    val isRunning: Boolean
        get() = running.get()

    @SuppressLint("MissingPermission")
    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        try {
            serverSocket =
                adapter.listenUsingRfcommWithServiceRecord(
                    SERVICE_NAME,
                    SERVICE_UUID,
                )
        } catch (throwable: Throwable) {
            running.set(false)
            throw throwable
        }

        worker = Thread(
            { acceptLoop() },
            "natsx-bluetooth-rfcomm",
        ).apply {
            isDaemon = true
            start()
        }
    }

    @SuppressLint("MissingPermission")
    private fun acceptLoop() {
        while (running.get()) {
            val socket = try {
                serverSocket?.accept() ?: break
            } catch (_: IOException) {
                if (running.get()) {
                    onError(
                        IOException(
                            "Bluetooth RFCOMM listener stopped unexpectedly.",
                        ),
                    )
                }
                break
            } catch (throwable: Throwable) {
                if (running.get()) {
                    onError(throwable)
                }
                break
            }

            if (!running.get()) {
                runCatching { socket.close() }
                .getOrNull()
                ?: Unit
                break
            }

            try {
                onConnection(socket)
            } catch (throwable: Throwable) {
                runCatching { socket.close() }
                onError(throwable)
            }
        }
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            return
        }

        runCatching { serverSocket?.close() }
        serverSocket = null

        worker?.interrupt()
        worker = null
    }

    companion object {
        const val SERVICE_NAME =
            "NATSX Controller"

        val SERVICE_UUID: UUID =
            UUID.fromString(
                "6f0f4f92-8d9d-4f7b-9bf0-1a6c4f6e4e36",
            )
    }
}
