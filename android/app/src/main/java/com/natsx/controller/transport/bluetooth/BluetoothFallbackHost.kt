package com.natsx.controller.transport.bluetooth

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothSocket
import com.natsx.controller.core.protocol.SessionId
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

class BluetoothFallbackHost(
    adapter: BluetoothAdapter,
    private val androidDeviceId: SessionId,
    private val trustResolver: (SessionId) -> ByteArray?,
) : AutoCloseable {
    private val running = AtomicBoolean(false)

    private val establishedQueue =
        ArrayBlockingQueue<EstablishedBluetoothSession>(
            1,
        )

    private val server =
        BluetoothRfcommServer(
            adapter = adapter,
            onConnection = { socket ->
                authenticate(socket)
            },
        )

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        try {
            server.start()
        } catch (throwable: Throwable) {
            running.set(false)
            throw throwable
        }
    }

    fun pollEstablished(
        timeoutMilliseconds: Long,
    ): EstablishedBluetoothSession? {
        require(timeoutMilliseconds >= 0)

        return establishedQueue.poll(
            timeoutMilliseconds,
            TimeUnit.MILLISECONDS,
        )
    }

    private fun authenticate(
        socket: BluetoothSocket,
    ) {
        if (!running.get()) {
            runCatching { socket.close() }
            return
        }

        val connection =
            BluetoothRfcommConnection(socket)

        val established = runCatching {
            BluetoothTrustedSessionClient(
                androidDeviceId =
                    androidDeviceId,
                trustResolver =
                    trustResolver,
            ).authenticate(connection)
        }.getOrElse {
            connection.close()
            return
        }

        val previous =
            establishedQueue.poll()

        previous?.connection?.close()
        previous?.trustedSession
            ?.sessionKey
            ?.fill(0)

        if (!establishedQueue.offer(established)) {
            established.connection.close()
            established.trustedSession
                .sessionKey
                .fill(0)
        }
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            return
        }

        server.close()

        while (true) {
            val pending =
                establishedQueue.poll()
                    ?: break

            pending.connection.close()
            pending.trustedSession
                .sessionKey
                .fill(0)
        }
    }
}
