package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.TransportPreferenceMode
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.SessionSequence
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

class BluetoothAutoReconnectRuntimeTest {
    @Test
    fun healthyHeartbeatKeepsBluetoothAttachedAsWarmSink() {
        val link =
            FakeLink(
                initialHeartbeat =
                    System.nanoTime(),
            )

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = SessionSequence(),
                monotonicMicros = { 1uL },
            )

        val runtime =
            BluetoothAutoReconnectRuntime(
                broadcaster = broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
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
                    BluetoothReconnectState.ACTIVE
            }

            assertEquals(
                BluetoothReconnectState.ACTIVE,
                runtime.state,
            )
            assertTrue(
                broadcaster.hasSinks(),
            )
        } finally {
            runtime.close()
        }

        assertTrue(link.closed.get())
    }

    @Test
    fun activeLinkReceivesTransportPreference() {
        val link =
            FakeLink(
                initialHeartbeat =
                    System.nanoTime(),
            )

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = SessionSequence(),
                monotonicMicros = { 1uL },
            )

        val runtime =
            BluetoothAutoReconnectRuntime(
                broadcaster = broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
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
                    BluetoothReconnectState.ACTIVE
            }

            val expected =
                TransportPreferencePayload(
                    TransportPreferenceMode.BLUETOOTH,
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
    fun repeatedHeartbeatLossRecoversAcrossFiveBluetoothCycles() {
        val createdLinks =
            java.util.concurrent
                .CopyOnWriteArrayList<FakeLink>()

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence =
                    SessionSequence(),
                monotonicMicros = {
                    1uL
                },
            )

        val runtime =
            BluetoothAutoReconnectRuntime(
                broadcaster = broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
                        FakeLink(
                            initialHeartbeat =
                                System.nanoTime(),
                        ).also(
                            createdLinks::add,
                        )
                    },
                nowNanos =
                    System::nanoTime,
                firstHeartbeatTimeoutMillis =
                    250,
                heartbeatLostTimeoutMillis =
                    500,
                pollIntervalMillis =
                    25,
            )

        try {
            runtime.start()

            repeat(5) { cycle ->
                waitUntil(1_500) {
                    createdLinks.size >=
                        cycle + 1 &&
                        runtime.state ==
                        BluetoothReconnectState.ACTIVE
                }

                val active =
                    createdLinks[cycle]

                active.lastHeartbeatReceivedNanos =
                    System.nanoTime() -
                        1_000_000_000L

                waitUntil(1_500) {
                    createdLinks.size >=
                        cycle + 2
                }

                assertTrue(
                    active.closed.get(),
                )
            }

            waitUntil(1_500) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE
            }

            assertTrue(
                runtime.reconnectAttempts >=
                    5,
            )
        } finally {
            runtime.close()
        }
    }

    @Test
    fun lostHeartbeatReconnectsWithoutResettingGlobalSequence() {
        val createCalls =
            AtomicInteger(0)

        val first =
            FakeLink(
                initialHeartbeat =
                    System.nanoTime(),
            )

        val second =
            FakeLink(
                initialHeartbeat =
                    System.nanoTime(),
            )

        val sequence =
            SessionSequence()

        val broadcaster =
            RealtimeStateBroadcaster(
                sequence = sequence,
                monotonicMicros = { 1uL },
            )

        val runtime =
            BluetoothAutoReconnectRuntime(
                broadcaster = broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
                        if (
                            createCalls
                                .incrementAndGet() == 1
                        ) {
                            first
                        } else {
                            second
                        }
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
                    BluetoothReconnectState.ACTIVE &&
                    createCalls.get() == 1
            }

            val beforeReconnect =
                broadcaster.publish(
                    GamepadState.Neutral,
                )

            assertEquals(
                0u,
                beforeReconnect?.sequence,
            )

            first.lastHeartbeatReceivedNanos =
                System.nanoTime() -
                    1_000_000_000L

            waitUntil(2_000) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE &&
                    createCalls.get() >= 2
            }

            val afterReconnect =
                broadcaster.publish(
                    GamepadState.Neutral,
                )

            assertEquals(
                1u,
                afterReconnect?.sequence,
            )
            assertTrue(
                runtime.reconnectAttempts >= 1,
            )
            assertTrue(first.closed.get())
            assertEquals(
                listOf(0u),
                first.sequences.toList(),
            )
            assertEquals(
                listOf(1u),
                second.sequences.toList(),
            )
        } finally {
            runtime.close()
        }
    }

    private fun waitUntil(
        timeoutMillis: Long,
        predicate: () -> Boolean,
    ) {
        val deadline =
            System.nanoTime() +
                timeoutMillis * 1_000_000L

        while (System.nanoTime() < deadline) {
            if (predicate()) {
                return
            }

            Thread.sleep(10)
        }

        check(predicate()) {
            "Condition was not met before timeout."
        }
    }

    private class FakeLink(
        initialHeartbeat: Long,
    ) : BluetoothRealtimeLink {
        @Volatile
        override var lastHeartbeatReceivedNanos:
            Long = initialHeartbeat

        val closed =
            AtomicBoolean(false)

        @Volatile
        var lastPreference:
            TransportPreferencePayload? = null

        val sequences =
            mutableListOf<UInt>()

        override fun trySendTransportPreference(
            payload: TransportPreferencePayload,
        ): Boolean {
            lastPreference = payload
            return true
        }

        override fun publish(
            envelope: RealtimeStateEnvelope,
        ) {
            @Suppress("UNUSED_VARIABLE")
            val state: GamepadState =
                envelope.state

            synchronized(sequences) {
                sequences.add(
                    envelope.sequence,
                )
            }
        }

        override fun close() {
            closed.set(true)
        }
    }
}
