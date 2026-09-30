package com.natsx.controller.transport.bluetooth

import android.bluetooth.BluetoothSocket
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.StreamFrameCodec
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.atomic.AtomicBoolean

class BluetoothRfcommConnection(
    private val socket: BluetoothSocket,
) : AutoCloseable {
    private val closed = AtomicBoolean(false)
    private val input: InputStream = socket.inputStream
    private val output: OutputStream = socket.outputStream

    val isConnected: Boolean
        get() = !closed.get() && socket.isConnected

    @Synchronized
    fun write(
        frame: ProtocolFrame,
        authenticationKey: ByteArray? = null,
    ) {
        check(!closed.get()) {
            "Bluetooth RFCOMM connection is closed."
        }

        StreamFrameCodec.write(
            output,
            frame,
            authenticationKey,
        )
    }

    fun read(
        authenticationKey: ByteArray? = null,
    ): ProtocolFrame {
        check(!closed.get()) {
            "Bluetooth RFCOMM connection is closed."
        }

        return StreamFrameCodec.read(
            input,
            authenticationKey,
        )
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        runCatching { socket.close() }
    }
}
