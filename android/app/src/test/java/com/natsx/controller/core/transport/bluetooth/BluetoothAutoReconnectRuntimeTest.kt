package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.gamepad.GamepadState
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
    fun reachesActiveAndAttachesWarmSink() {
        val createCalls =
            AtomicInteger(0)
        val link =
            FakeLink(
                System.nanoTime(),
            )

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
                broadcaster =
                    broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
                        createCalls.incrementAndGet()
                        link
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

            waitUntil(1_000) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE
            }

            assertEquals(
                BluetoothReconnectState.ACTIVE,
                runtime.state,
            )
            assertEquals(
                1,
                createCalls.get(),
            )
            assertTrue(
                broadcaster.hasSinks(),
            )
        } finally {
            runtime.close()
        }

        assertTrue(
            link.closed.get(),
        )
    }

    @Test
    fun lostHeartbeatCreatesFreshLinkAutomatically() {
        val createCalls =
            AtomicInteger(0)

        val first =
            FakeLink(
                System.nanoTime(),
            )
        val second =
            FakeLink(
                System.nanoTime(),
            )

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
                broadcaster =
                    broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
                        if (
                            createCalls
                                .getAndIncrement() == 0
                        ) {
                            first
                        } else {
                            second
                        }
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

            waitUntil(1_000) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE &&
                    createCalls.get() == 1
            }

            first.lastHeartbeatReceivedNanos =
                System.nanoTime() -
                    1_000_000_000L

            waitUntil(2_000) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE &&
                    createCalls.get() >= 2
            }

            assertTrue(
                first.closed.get(),
            )
            assertTrue(
                runtime.reconnectAttempts >= 1,
            )
            assertTrue(
                broadcaster.hasSinks(),
            )
        } finally {
            runtime.close()
        }
    }

    @Test
    fun failedCreateRetriesWithBackoff() {
        val createCalls =
            AtomicInteger(0)

        val recovered =
            FakeLink(
                System.nanoTime(),
            )

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
                broadcaster =
                    broadcaster,
                linkFactory =
                    BluetoothRealtimeLinkFactory {
                        if (
                            createCalls
                                .incrementAndGet() < 2
                        ) {
                            error(
                                "simulated connect failure",
                            )
                        }

                        recovered
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

            waitUntil(2_000) {
                runtime.state ==
                    BluetoothReconnectState.ACTIVE
            }

            assertTrue(
                createCalls.get() >= 2,
            )
            assertTrue(
                runtime.reconnectAttempts >= 1,
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
                timeoutMillis *
                1_000_000L

        while (
            System.nanoTime() <
            deadline
        ) {
            if (predicate()) return
            Thread.sleep(10)
        }

        check(predicate()) {
            "Condition was not met before timeout."
        }
    }

    private class FakeLink(
        initialHeartbeat: Long,
    ) : BluetoothRealtimeLink {
        override var lastHeartbeatReceivedNanos:
            Long =
            initialHeartbeat

        val closed =
            AtomicBoolean(false)

        override fun publish(
            envelope: RealtimeStateEnvelope,
        ) {
            @Suppress("UNUSED_VARIABLE")
            val state: GamepadState =
                envelope.state
        }

        override fun close() {
            closed.set(true)
        }
    }
}
