package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import java.io.Closeable
import java.net.InetSocketAddress
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

enum class WifiReconnectState {
    IDLE,
    RESOLVING,
    CONNECTING,
    AWAITING_HEARTBEAT,
    ACTIVE,
    RECONNECTING,
    STOPPED,
}

/**
 * Owns the Android Wi-Fi link lifecycle after a trusted session exists.
 *
 * The runtime always tries the endpoint provider's fast path first. The
 * production provider performs an authenticated cached-endpoint probe and
 * falls back to trusted-peer LAN discovery when necessary.
 *
 * When heartbeats stop, this runtime removes the stale sink, closes the UDP
 * link, and resolves again without resetting the session-global sequence.
 */
class WifiAutoReconnectRuntime(
    private val broadcaster: RealtimeStateBroadcaster,
    private val endpointProvider: WifiEndpointProvider,
    private val linkFactory: WifiRealtimeLinkFactory,
    private val nowNanos: () -> Long = SystemClock::elapsedRealtimeNanos,
    private val firstHeartbeatTimeoutMillis: Long =
        DEFAULT_FIRST_HEARTBEAT_TIMEOUT_MILLIS,
    private val heartbeatLostTimeoutMillis: Long =
        DEFAULT_HEARTBEAT_LOST_TIMEOUT_MILLIS,
    private val pollIntervalMillis: Long = DEFAULT_POLL_INTERVAL_MILLIS,
) : Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-wifi-reconnect").apply {
                isDaemon = true
            }
        }

    private val running = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)
    private val activeLink = AtomicReference<WifiRealtimeLink?>(null)

    @Volatile
    var state: WifiReconnectState = WifiReconnectState.IDLE
        private set

    @Volatile
    var activeEndpoint: InetSocketAddress? = null
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
        check(!closed.get()) { "Wi-Fi reconnect runtime is closed." }

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
        state = WifiReconnectState.STOPPED
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        running.set(false)
        detachActiveLink()
        executor.shutdownNow()
        state = WifiReconnectState.STOPPED
    }

    private fun runLoop() {
        var retryIndex = 0

        while (running.get() && !closed.get()) {
            state = if (retryIndex == 0) {
                WifiReconnectState.RESOLVING
            } else {
                WifiReconnectState.RECONNECTING
            }

            val resolved =
                runCatching { endpointProvider.resolve() }
                    .getOrNull()

            if (resolved == null) {
                reconnectAttempts += 1
                sleepBackoff(retryIndex++)
                continue
            }

            state = WifiReconnectState.CONNECTING
            activeEndpoint = resolved.endpoint

            val link =
                runCatching { linkFactory.create(resolved.endpoint) }
                    .getOrNull()

            if (link == null) {
                activeEndpoint = null
                reconnectAttempts += 1
                sleepBackoff(retryIndex++)
                continue
            }

            activeLink.set(link)
            broadcaster.addSink(link)
            state = WifiReconnectState.AWAITING_HEARTBEAT

            val attachedAt = nowNanos()
            var heartbeatObserved = false

            try {
                while (running.get() && !closed.get()) {
                    val now = nowNanos()
                    val lastHeartbeat = link.lastHeartbeatReceivedNanos

                    if (lastHeartbeat > 0L) {
                        heartbeatObserved = true
                        state = WifiReconnectState.ACTIVE
                        retryIndex = 0

                        if (
                            elapsedMillis(lastHeartbeat, now) >
                            heartbeatLostTimeoutMillis
                        ) {
                            break
                        }
                    } else if (
                        elapsedMillis(attachedAt, now) >
                        firstHeartbeatTimeoutMillis
                    ) {
                        break
                    }

                    sleepInterruptibly(pollIntervalMillis)
                }
            } finally {
                broadcaster.removeSink(link)
                activeLink.compareAndSet(link, null)
                runCatching { link.close() }
                activeEndpoint = null
            }

            if (!running.get() || closed.get()) {
                break
            }

            reconnectAttempts += 1

            if (heartbeatObserved) {
                retryIndex = 0
            }

            state = WifiReconnectState.RECONNECTING
            sleepBackoff(retryIndex++)
        }

        if (!closed.get()) {
            state = WifiReconnectState.STOPPED
        }
    }

    private fun detachActiveLink() {
        activeLink.getAndSet(null)?.let { link ->
            broadcaster.removeSink(link)
            runCatching { link.close() }
        }

        activeEndpoint = null
    }

    private fun sleepBackoff(retryIndex: Int) {
        val delay =
            RECONNECT_BACKOFF_MILLIS[
                retryIndex.coerceIn(
                    0,
                    RECONNECT_BACKOFF_MILLIS.lastIndex,
                )
            ]

        sleepInterruptibly(delay)
    }

    private fun sleepInterruptibly(millis: Long) {
        if (millis <= 0) return

        try {
            Thread.sleep(millis)
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }

    private fun elapsedMillis(
        startNanos: Long,
        endNanos: Long,
    ): Long {
        if (endNanos <= startNanos) return 0
        return (endNanos - startNanos) / 1_000_000L
    }

    companion object {
        const val DEFAULT_FIRST_HEARTBEAT_TIMEOUT_MILLIS = 1_500L
        const val DEFAULT_HEARTBEAT_LOST_TIMEOUT_MILLIS = 1_250L
        const val DEFAULT_POLL_INTERVAL_MILLIS = 250L

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
