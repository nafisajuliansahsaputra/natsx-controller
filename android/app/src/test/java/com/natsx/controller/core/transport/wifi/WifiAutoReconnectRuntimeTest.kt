package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TransportPreferenceMode
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.SessionSequence
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

class WifiAutoReconnectRuntimeTest {
    @Test
    fun reachesActiveUsingResolvedTrustedEndpoint() {
        val endpoint =
            InetSocketAddress(
                InetAddress.getByName("127.0.0.1"),
                43860,
            )
        val resolveCalls = AtomicInteger(0)
        val link = FakeLink(System.nanoTime())

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = SessionSequence(),
                monotonicMicros = { 1uL },
            )

        val runtime =
            WifiAutoReconnectRuntime(
                broadcaster = broadcaster,
                endpointProvider = WifiEndpointProvider {
                    resolveCalls.incrementAndGet()
                    WifiResolvedEndpoint(
                        endpoint = endpoint,
                        source = WifiEndpointResolutionSource.CACHED_DIRECT,
                    )
                },
                linkFactory = WifiRealtimeLinkFactory { link },
                nowNanos = System::nanoTime,
                firstHeartbeatTimeoutMillis = 250,
                heartbeatLostTimeoutMillis = 500,
                pollIntervalMillis = 25,
            )

        try {
            runtime.start()

            waitUntil(1_000) {
                runtime.state == WifiReconnectState.ACTIVE
            }

            assertEquals(WifiReconnectState.ACTIVE, runtime.state)
            assertEquals(endpoint, runtime.activeEndpoint)
            assertEquals(1, resolveCalls.get())
            assertTrue(broadcaster.hasSinks())
        } finally {
            runtime.close()
        }

        assertTrue(link.closed.get())
    }

    @Test
    fun activeLinkReceivesTransportPreference() {
        val endpoint =
            InetSocketAddress(
                InetAddress.getByName("127.0.0.1"),
                43860,
            )
        val link =
            FakeLink(
                System.nanoTime(),
            )
        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = SessionSequence(),
                monotonicMicros = { 1uL },
            )
        val runtime =
            WifiAutoReconnectRuntime(
                broadcaster = broadcaster,
                endpointProvider =
                    WifiEndpointProvider {
                        WifiResolvedEndpoint(
                            endpoint = endpoint,
                            source =
                                WifiEndpointResolutionSource.CACHED_DIRECT,
                        )
                    },
                linkFactory =
                    WifiRealtimeLinkFactory {
                        link
                    },
                nowNanos = System::nanoTime,
                firstHeartbeatTimeoutMillis = 250,
                heartbeatLostTimeoutMillis = 500,
                pollIntervalMillis = 25,
            )

        try {
            runtime.start()

            waitUntil(1_000) {
                runtime.state ==
                    WifiReconnectState.ACTIVE
            }

            val expected =
                TransportPreferencePayload(
                    TransportPreferenceMode.WIFI,
                )

            assertTrue(
                runtime.trySendTransportPreference(
                    expected,
                ),
            )
            assertEquals(
                expected,
                link.lastPreference,
            )
        } finally {
            runtime.close()
        }
    }

    @Test
    fun lostHeartbeatTriggersAutomaticResolutionAgain() {
        val firstEndpoint =
            InetSocketAddress(
                InetAddress.getByName("127.0.0.1"),
                43860,
            )
        val secondEndpoint =
            InetSocketAddress(
                InetAddress.getByName("127.0.0.2"),
                43860,
            )
        val resolveCalls = AtomicInteger(0)
        val first = FakeLink(System.nanoTime())
        val second = FakeLink(System.nanoTime())

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = SessionSequence(),
                monotonicMicros = { 1uL },
            )

        val runtime =
            WifiAutoReconnectRuntime(
                broadcaster = broadcaster,
                endpointProvider = WifiEndpointProvider {
                    val call = resolveCalls.incrementAndGet()
                    WifiResolvedEndpoint(
                        endpoint =
                            if (call == 1) {
                                firstEndpoint
                            } else {
                                secondEndpoint
                            },
                        source = WifiEndpointResolutionSource.CACHED_DIRECT,
                    )
                },
                linkFactory = WifiRealtimeLinkFactory { endpoint ->
                    if (endpoint == firstEndpoint) first else second
                },
                nowNanos = System::nanoTime,
                firstHeartbeatTimeoutMillis = 250,
                heartbeatLostTimeoutMillis = 500,
                pollIntervalMillis = 25,
            )

        try {
            runtime.start()

            waitUntil(1_000) {
                runtime.state == WifiReconnectState.ACTIVE &&
                    runtime.activeEndpoint == firstEndpoint
            }

            first.lastHeartbeatReceivedNanos =
                System.nanoTime() - 1_000_000_000L

            waitUntil(2_000) {
                runtime.state == WifiReconnectState.ACTIVE &&
                    runtime.activeEndpoint == secondEndpoint
            }

            assertTrue(resolveCalls.get() >= 2)
            assertTrue(runtime.reconnectAttempts >= 1)
            assertTrue(first.closed.get())
        } finally {
            runtime.close()
        }
    }

    private fun waitUntil(
        timeoutMillis: Long,
        predicate: () -> Boolean,
    ) {
        val deadline = System.nanoTime() + timeoutMillis * 1_000_000L

        while (System.nanoTime() < deadline) {
            if (predicate()) return
            Thread.sleep(10)
        }

        check(predicate()) { "Condition was not met before timeout." }
    }

    private class FakeLink(
        initialHeartbeat: Long,
    ) : WifiRealtimeLink {
        override var lastHeartbeatReceivedNanos: Long = initialHeartbeat
        val closed = AtomicBoolean(false)

        @Volatile
        var lastPreference:
            TransportPreferencePayload? = null

        override fun trySendTransportPreference(
            payload: TransportPreferencePayload,
        ): Boolean {
            lastPreference = payload
            return true
        }

        override fun publish(envelope: RealtimeStateEnvelope) {
            @Suppress("UNUSED_VARIABLE")
            val state: GamepadState = envelope.state
        }

        override fun close() {
            closed.set(true)
        }
    }
}
