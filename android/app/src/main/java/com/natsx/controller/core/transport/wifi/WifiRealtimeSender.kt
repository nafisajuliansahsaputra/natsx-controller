package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.gamepad.GamepadState
import java.io.Closeable
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

class WifiRealtimeSender(
    private val remoteEndpoint: InetSocketAddress,
    private val trustedSession: WifiTrustedSession,
    private val keepAliveIntervalMillis: Long = DEFAULT_KEEPALIVE_MILLIS,
) : Closeable {
    private val executor: ScheduledExecutorService =
        Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(runnable, "natsx-wifi-realtime").apply {
                isDaemon = true
                priority = Thread.NORM_PRIORITY + 1
            }
        }

    private val socketLock = Any()
    private var socket: DatagramSocket? = null

    private val latestState = AtomicReference(GamepadState.Neutral)
    private val pendingState = AtomicReference<GamepadState?>(null)
    private val drainScheduled = AtomicBoolean(false)
    private val sequence = AtomicInteger(0)
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

    init {
        require(!remoteEndpoint.address.isAnyLocalAddress) {
            "Remote endpoint must identify the trusted receiver."
        }
        require(keepAliveIntervalMillis in MIN_KEEPALIVE_MILLIS..MAX_KEEPALIVE_MILLIS) {
            "Keepalive interval is outside the supported range."
        }

        executor.scheduleWithFixedDelay(
            ::scheduleKeepAlive,
            keepAliveIntervalMillis,
            keepAliveIntervalMillis,
            TimeUnit.MILLISECONDS,
        )
    }

    /**
     * Publishes the newest full controller state.
     *
     * This path never creates an unbounded event queue. If state changes faster
     * than UDP can send, intermediate snapshots are replaced by the newest one.
     */
    fun publish(state: GamepadState) {
        check(!closed.get()) { "Wi-Fi realtime sender is closed." }

        latestState.set(state)
        enqueueLatest(state)
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        pendingState.set(null)

        synchronized(socketLock) {
            socket?.close()
            socket = null
        }

        executor.shutdownNow()
        trustedSession.close()
    }

    private fun scheduleKeepAlive() {
        if (closed.get()) {
            return
        }

        // A periodic full-state packet doubles as liveness traffic and ensures
        // a recovered Wi-Fi path retries automatically even when the user's
        // fingers have not moved since the network interruption.
        enqueueLatest(latestState.get())
    }

    private fun enqueueLatest(state: GamepadState) {
        pendingState.set(state)

        if (drainScheduled.compareAndSet(false, true)) {
            executor.execute(::drainLatest)
        }
    }

    private fun drainLatest() {
        while (!closed.get()) {
            val state = pendingState.getAndSet(null)

            if (state != null) {
                send(state)
            }

            drainScheduled.set(false)

            if (
                pendingState.get() != null &&
                drainScheduled.compareAndSet(false, true)
            ) {
                continue
            }

            return
        }

        drainScheduled.set(false)
    }

    private fun send(state: GamepadState) {
        try {
            val nextSequence = sequence.getAndIncrement().toUInt()
            val timestampMicros =
                SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL

            val bytes = WifiRealtimeDatagramEncoder.encodeGamepadState(
                state = state,
                trustedSession = trustedSession,
                sequence = nextSequence,
                monotonicTimestampMicros = timestampMicros,
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

    private fun ensureSocket(): DatagramSocket {
        synchronized(socketLock) {
            check(!closed.get()) { "Wi-Fi realtime sender is closed." }

            val existing = socket
            if (existing != null && !existing.isClosed) {
                return existing
            }

            val reopened = DatagramSocket().apply {
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

    companion object {
        const val DEFAULT_KEEPALIVE_MILLIS = 100L
        const val MIN_KEEPALIVE_MILLIS = 25L
        const val MAX_KEEPALIVE_MILLIS = 1_000L
    }
}
