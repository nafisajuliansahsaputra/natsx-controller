package com.natsx.controller.core.connection

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateListener
import com.natsx.controller.core.gamepad.GamepadStateStore
import java.util.concurrent.Executor
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

fun interface GamepadStateSender {
    fun send(state: GamepadState)
}

class LatestGamepadPublisher(
    private val stateStore: GamepadStateStore,
    private val sender: GamepadStateSender,
    private val executor: Executor = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "natsx-gamepad-publisher").apply {
            isDaemon = true
        }
    },
) : AutoCloseable {
    private val pending = AtomicReference<GamepadState?>(null)
    private val drainScheduled = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)

    private val listener = GamepadStateListener { state ->
        publish(state)
    }

    fun start(sendInitialState: Boolean = true) {
        check(!closed.get()) {
            "Publisher is closed."
        }

        stateStore.addListener(listener)

        if (sendInitialState) {
            publish(stateStore.snapshot())
        }
    }

    fun publish(state: GamepadState) {
        if (closed.get()) {
            return
        }

        pending.set(state)
        scheduleDrain()
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        stateStore.removeListener(listener)
        pending.set(null)

        if (executor is ExecutorService) {
            executor.shutdownNow()
        }
    }

    private fun scheduleDrain() {
        if (!drainScheduled.compareAndSet(false, true)) {
            return
        }

        executor.execute {
            drain()
        }
    }

    private fun drain() {
        try {
            while (!closed.get()) {
                val state = pending.getAndSet(null) ?: break
                sender.send(state)
            }
        } finally {
            drainScheduled.set(false)

            if (!closed.get() &&
                pending.get() != null &&
                drainScheduled.compareAndSet(false, true)
            ) {
                executor.execute {
                    drain()
                }
            }
        }
    }
}
