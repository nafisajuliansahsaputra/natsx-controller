package com.natsx.controller.core.connection

import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateStore
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class RealtimeGamepadPublisherTest {
    @Test
    fun unchangedHeldInputIsResentAtRealtimeRate() {
        val store = GamepadStateStore()
        store.setButton(
            GamepadButtons.A,
            true,
        )

        val count = AtomicInteger(0)
        val latch = CountDownLatch(3)
        val states =
            mutableListOf<GamepadState>()
        val lock = Any()

        val publisher =
            RealtimeGamepadPublisher(
                stateStore = store,
                sender = GamepadStateSender {
                    synchronized(lock) {
                        states += it
                    }
                    count.incrementAndGet()
                    latch.countDown()
                },
                rateHz = 120,
            )

        publisher.start()

        assertTrue(
            latch.await(
                500,
                TimeUnit.MILLISECONDS,
            ),
        )

        publisher.close()

        assertTrue(count.get() >= 3)

        synchronized(lock) {
            assertTrue(
                states.all {
                    it.buttons and
                        GamepadButtons.A != 0
                },
            )
        }
    }

    @Test
    fun latestSnapshotWinsAfterStateChange() {
        val store = GamepadStateStore()
        val latch = CountDownLatch(1)
        val lastTrigger = AtomicInteger(-1)

        val publisher =
            RealtimeGamepadPublisher(
                stateStore = store,
                sender = GamepadStateSender {
                    lastTrigger.set(
                        it.rightTrigger,
                    )
                    if (it.rightTrigger == 220) {
                        latch.countDown()
                    }
                },
                rateHz = 120,
            )

        publisher.start()
        store.setRightTrigger(220)

        assertTrue(
            latch.await(
                500,
                TimeUnit.MILLISECONDS,
            ),
        )

        publisher.close()

        assertEquals(
            220,
            lastTrigger.get(),
        )
    }
}
