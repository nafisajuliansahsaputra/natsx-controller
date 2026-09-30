package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.gamepad.GamepadState
import java.io.Closeable
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

class WifiRealtimeSender(
    private val remoteEndpoint: InetSocketAddress,
    private val trustedSession: WifiTrustedSession,
) : Closeable {
    private val socket = DatagramSocket()
    private val executor = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "natsx-wifi-realtime").apply {
            isDaemon = true
            priority = Thread.NORM_PRIORITY + 1
        }
    }

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

    init {
        require(!remoteEndpoint.address.isAnyLocalAddress) {
            "Remote endpoint must identify the trusted receiver."
        }
        socket.connect(remoteEndpoint)
    }

    /**
     * Publishes the newest full controller state.
     *
     * This path never creates an unbounded event queue. If state changes faster
     * than UDP can send, intermediate snapshots are replaced by the newest one.
     */
    fun publish(state: GamepadState) {
        check(!closed.get()) { "Wi-Fi realtime sender is closed." }

        pendingState.set(state)

        if (drainScheduled.compareAndSet(false, true)) {
            executor.execute(::drainLatest)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        pendingState.set(null)
        socket.close()
        executor.shutdownNow()
        trustedSession.close()
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

            socket.send(
                DatagramPacket(
                    bytes,
                    bytes.size,
                    remoteEndpoint,
                ),
            )

            sentDatagrams += 1
        } catch (_: Exception) {
            if (!closed.get()) {
                sendFailures += 1
            }
        }
    }
}
