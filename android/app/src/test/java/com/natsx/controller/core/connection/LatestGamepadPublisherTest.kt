package com.natsx.controller.core.connection

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateStore
import java.util.ArrayDeque
import java.util.concurrent.Executor
import org.junit.Assert.assertEquals
import org.junit.Test

class LatestGamepadPublisherTest {
    @Test
    fun multiplePendingChangesCollapseToLatestState() {
        val store = GamepadStateStore()
        val executor = QueueExecutor()
        val sent = mutableListOf<GamepadState>()

        val publisher = LatestGamepadPublisher(
            stateStore = store,
            sender = GamepadStateSender { sent += it },
            executor = executor,
        )

        publisher.start(sendInitialState = false)

        store.setLeftTrigger(10)
        store.setLeftTrigger(20)
        store.setLeftTrigger(30)

        assertEquals(1, executor.size)
        executor.runNext()

        assertEquals(1, sent.size)
        assertEquals(30, sent.single().leftTrigger)

        publisher.close()
    }

    @Test
    fun publisherSendsFreshChangeAfterDrain() {
        val store = GamepadStateStore()
        val executor = QueueExecutor()
        val sent = mutableListOf<GamepadState>()

        val publisher = LatestGamepadPublisher(
            stateStore = store,
            sender = GamepadStateSender { sent += it },
            executor = executor,
        )

        publisher.start(sendInitialState = false)

        store.setRightTrigger(50)
        executor.runNext()

        store.setRightTrigger(200)
        executor.runNext()

        assertEquals(listOf(50, 200), sent.map { it.rightTrigger })

        publisher.close()
    }

    private class QueueExecutor : Executor {
        private val queue = ArrayDeque<Runnable>()

        val size: Int
            get() = queue.size

        override fun execute(command: Runnable) {
            queue += command
        }

        fun runNext() {
            queue.removeFirst().run()
        }
    }
}
