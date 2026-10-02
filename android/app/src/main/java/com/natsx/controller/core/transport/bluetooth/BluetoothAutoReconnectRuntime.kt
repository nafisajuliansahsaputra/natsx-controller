package com.natsx.controller.core.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import java.io.Closeable
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

enum class BluetoothReconnectState {
    IDLE,
    CONNECTING,
    AWAITING_HEARTBEAT,
    ACTIVE,
    RECONNECTING,
    STOPPED,
}

/**
 * Keeps a trusted Bluetooth RFCOMM transport warm while another transport may
 * remain authoritative.
 *
 * The runtime does not own SessionSequence. It only attaches/removes a
 * Bluetooth realtime sink from the shared RealtimeStateBroadcaster, so every
 * transport receives the same logical controller revision.
 */
class BluetoothAutoReconnectRuntime(
    private val broadcaster: RealtimeStateBroadcaster,
    private val linkFactory: BluetoothRealtimeLinkFactory,
    private val nowNanos: () -> Long = SystemClock::elapsedRealtimeNanos,
    private val firstHeartbeatTimeoutMillis: Long =
        DEFAULT_FIRST_HEARTBEAT_TIMEOUT_MILLIS,
    private val heartbeatLostTimeoutMillis: Long =
        DEFAULT_HEARTBEAT_LOST_TIMEOUT_MILLIS,
    private val pollIntervalMillis: Long =
        DEFAULT_POLL_INTERVAL_MILLIS,
) : Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(
                runnable,
                "natsx-bluetooth-reconnect",
            ).apply {
                isDaemon = true
            }
        }

    private val running = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)

    private val activeLink =
        AtomicReference<BluetoothRealtimeLink?>(null)

    @Volatile
    var state: BluetoothReconnectState =
        BluetoothReconnectState.IDLE
        private set

    @Volatile
    var reconnectAttempts: Long = 0
        private set

    init {
        require(firstHeartbeatTimeoutMillis in 250..10_000)
        require(heartbeatLostTimeoutMillis in 500..30_000)
        require(pollIntervalMillis in 25..1_000)
    }

    fun trySendTransportPreference(
        payload: TransportPreferencePayload,
    ): Boolean =
        activeLink
            .get()
            ?.trySendTransportPreference(
                payload,
            )
            ?: false

    fun start() {
        check(!closed.get()) {
            "Bluetooth reconnect runtime is closed."
        }

        if (!running.compareAndSet(false, true)) {
            return
        }

        executor.execute(::runLoop)
    }

    fun stop() {
        if (!running.compareAndSet(true, false)) {
            return
        }

        detachActiveLink()
        state = BluetoothReconnectState.STOPPED
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        running.set(false)
        detachActiveLink()
        executor.shutdownNow()
        state = BluetoothReconnectState.STOPPED
    }

    private fun runLoop() {
        var retryIndex = 0

        while (running.get() && !closed.get()) {
            state =
                if (retryIndex == 0) {
                    BluetoothReconnectState.CONNECTING
                } else {
                    BluetoothReconnectState.RECONNECTING
                }

            val link =
                runCatching {
                    linkFactory.create()
                }.getOrNull()

            if (link == null) {
                reconnectAttempts += 1
                sleepBackoff(retryIndex++)
                continue
            }

            activeLink.set(link)
            broadcaster.addSink(link)
            state =
                BluetoothReconnectState.AWAITING_HEARTBEAT

            val attachedAt = nowNanos()
            var heartbeatObserved = false

            try {
                while (running.get() && !closed.get()) {
                    val now = nowNanos()
                    val lastHeartbeat =
                        link.lastHeartbeatReceivedNanos

                    if (lastHeartbeat > 0L) {
                        heartbeatObserved = true
                        state =
                            BluetoothReconnectState.ACTIVE
                        retryIndex = 0

                        if (
                            elapsedMillis(
                                lastHeartbeat,
                                now,
                            ) > heartbeatLostTimeoutMillis
                        ) {
                            break
                        }
                    } else if (
                        elapsedMillis(
                            attachedAt,
                            now,
                        ) > firstHeartbeatTimeoutMillis
                    ) {
                        break
                    }

                    sleepInterruptibly(
                        pollIntervalMillis,
                    )
                }
            } finally {
                broadcaster.removeSink(link)
                activeLink.compareAndSet(
                    link,
                    null,
                )
                runCatching {
                    link.close()
                }
            }

            if (!running.get() || closed.get()) {
                break
            }

            reconnectAttempts += 1

            if (heartbeatObserved) {
                retryIndex = 0
            }

            state =
                BluetoothReconnectState.RECONNECTING

            sleepBackoff(retryIndex++)
        }

        if (!closed.get()) {
            state =
                BluetoothReconnectState.STOPPED
        }
    }

    private fun detachActiveLink() {
        activeLink.getAndSet(null)
            ?.let { link ->
                broadcaster.removeSink(link)
                runCatching {
                    link.close()
                }
            }
    }

    private fun sleepBackoff(
        retryIndex: Int,
    ) {
        val delay =
            RECONNECT_BACKOFF_MILLIS[
                retryIndex.coerceIn(
                    0,
                    RECONNECT_BACKOFF_MILLIS.lastIndex,
                )
            ]

        sleepInterruptibly(delay)
    }

    private fun sleepInterruptibly(
        millis: Long,
    ) {
        if (millis <= 0) {
            return
        }

        try {
            Thread.sleep(millis)
        } catch (_: InterruptedException) {
            Thread.currentThread()
                .interrupt()
        }
    }

    private fun elapsedMillis(
        startNanos: Long,
        endNanos: Long,
    ): Long {
        if (endNanos <= startNanos) {
            return 0
        }

        return (endNanos - startNanos) /
            1_000_000L
    }

    companion object {
        const val DEFAULT_FIRST_HEARTBEAT_TIMEOUT_MILLIS =
            1_500L
        const val DEFAULT_HEARTBEAT_LOST_TIMEOUT_MILLIS =
            2_000L
        const val DEFAULT_POLL_INTERVAL_MILLIS =
            250L

        val RECONNECT_BACKOFF_MILLIS =
            longArrayOf(
                0,
                250,
                500,
                1_000,
                2_000,
                4_000,
                8_000,
                15_000,
            )
    }
}
