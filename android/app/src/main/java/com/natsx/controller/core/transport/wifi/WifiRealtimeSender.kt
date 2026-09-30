package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class WifiRealtimeSender(
    private val remoteEndpoint: InetSocketAddress,
    private val trustedSession: WifiTrustedSession,
) : Closeable, RealtimeStateSink {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-wifi-realtime").apply {
                isDaemon = true
                priority = Thread.NORM_PRIORITY + 1
            }
        }

    private val receiveExecutor =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-wifi-control").apply {
                isDaemon = true
            }
        }

    private val socketLock = Any()
    private var socket: DatagramSocket? = null

    private val pendingEnvelope = AtomicReference<RealtimeStateEnvelope?>(null)
    private val drainScheduled = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)

    @Volatile
    var sentDatagrams: Long = 0
        private set

    @Volatile
    var sendFailures: Long = 0
        private set

    @Volatile
    var socketReopens: Long = 0
        private set

    @Volatile
    var heartbeatsReceived: Long = 0
        private set

    @Volatile
    var heartbeatAcksSent: Long = 0
        private set

    @Volatile
    var controlFailures: Long = 0
        private set

    @Volatile
    var lastHeartbeatReceivedNanos: Long = 0
        private set

    init {
        require(!remoteEndpoint.address.isAnyLocalAddress) {
            "Remote endpoint must identify the trusted receiver."
        }

        receiveExecutor.execute(::receiveControlLoop)
    }

    /**
     * Queues the newest session-global full-state revision.
     *
     * Every ready transport receives the same envelope from
     * ControllerRealtimePublisher. Wi-Fi never allocates its own sequence.
     */
    override fun publish(envelope: RealtimeStateEnvelope) {
        check(!closed.get()) { "Wi-Fi realtime sender is closed." }

        pendingEnvelope.set(envelope)

        if (drainScheduled.compareAndSet(false, true)) {
            executor.execute(::drainLatest)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        pendingEnvelope.set(null)

        synchronized(socketLock) {
            socket?.close()
            socket = null
        }

        executor.shutdownNow()
        receiveExecutor.shutdownNow()

        // WifiTrustedSession belongs to the logical controller session and may
        // be reused when this transport reconnects at a new endpoint.
    }

    private fun drainLatest() {
        while (!closed.get()) {
            val envelope = pendingEnvelope.getAndSet(null)

            if (envelope != null) {
                send(envelope)
            }

            drainScheduled.set(false)

            if (
                pendingEnvelope.get() != null &&
                drainScheduled.compareAndSet(false, true)
            ) {
                continue
            }

            return
        }

        drainScheduled.set(false)
    }

    private fun send(envelope: RealtimeStateEnvelope) {
        try {
            val bytes = WifiRealtimeDatagramEncoder.encodeGamepadState(
                state = envelope.state,
                trustedSession = trustedSession,
                sequence = envelope.sequence,
                monotonicTimestampMicros = envelope.monotonicTimestampMicros,
            )

            val activeSocket = ensureSocket()
            activeSocket.send(
                DatagramPacket(
                    bytes,
                    bytes.size,
                    remoteEndpoint,
                ),
            )

            sentDatagrams += 1
        } catch (_: IOException) {
            if (!closed.get()) {
                sendFailures += 1
                invalidateSocket()
            }
        } catch (_: RuntimeException) {
            if (!closed.get()) {
                sendFailures += 1
            }
        }
    }

    private fun receiveControlLoop() {
        val receiveBuffer = ByteArray(MAXIMUM_DATAGRAM_SIZE)

        while (!closed.get()) {
            try {
                val activeSocket = ensureSocket()
                val packet = DatagramPacket(
                    receiveBuffer,
                    receiveBuffer.size,
                )

                activeSocket.receive(packet)

                val datagram = packet.data.copyOfRange(
                    packet.offset,
                    packet.offset + packet.length,
                )

                val echoedProbeTimestamp =
                    WifiControlDatagramCodec.decodeHeartbeat(
                        datagram,
                        trustedSession,
                    )

                heartbeatsReceived += 1
                lastHeartbeatReceivedNanos = SystemClock.elapsedRealtimeNanos()

                val ack =
                    WifiControlDatagramCodec.encodeHeartbeatAck(
                        trustedSession = trustedSession,
                        responderTimestampMicros = monotonicMicroseconds(),
                        echoedProbeTimestampMicros = echoedProbeTimestamp,
                    )

                activeSocket.send(
                    DatagramPacket(
                        ack,
                        ack.size,
                        remoteEndpoint,
                    ),
                )

                heartbeatAcksSent += 1
            } catch (_: SocketTimeoutException) {
                // Periodically wake so close/recovery can be observed.
            } catch (_: IOException) {
                if (!closed.get()) {
                    controlFailures += 1
                    invalidateSocket()
                    sleepBeforeReconnect()
                }
            } catch (_: IllegalArgumentException) {
                if (!closed.get()) {
                    controlFailures += 1
                }
            } catch (_: IllegalStateException) {
                if (!closed.get()) {
                    controlFailures += 1
                }
            }
        }
    }

    private fun ensureSocket(): DatagramSocket {
        synchronized(socketLock) {
            check(!closed.get()) { "Wi-Fi realtime sender is closed." }

            val existing = socket
            if (existing != null && !existing.isClosed) {
                return existing
            }

            val reopened = DatagramSocket().apply {
                soTimeout = RECEIVE_TIMEOUT_MILLIS
                connect(remoteEndpoint)
            }

            socket = reopened
            socketReopens += 1
            return reopened
        }
    }

    private fun invalidateSocket() {
        synchronized(socketLock) {
            socket?.close()
            socket = null
        }
    }

    private fun sleepBeforeReconnect() {
        try {
            Thread.sleep(RECONNECT_RETRY_MILLIS)
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }

    private fun monotonicMicroseconds(): ULong =
        SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL

    companion object {
        const val RECEIVE_TIMEOUT_MILLIS = 1_000
        const val RECONNECT_RETRY_MILLIS = 250L
        const val MAXIMUM_DATAGRAM_SIZE = 512
    }
}
