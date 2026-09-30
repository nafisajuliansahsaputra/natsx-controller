package com.natsx.controller.core.connection

import com.natsx.controller.core.gamepad.GamepadStateStore
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.locks.LockSupport

class RealtimeGamepadPublisher(
    private val stateStore: GamepadStateStore,
    private val sender: GamepadStateSender,
    rateHz: Int = DEFAULT_RATE_HZ,
    private val onError: (Throwable) -> Unit = {},
) : AutoCloseable {
    private val running = AtomicBoolean(false)

    private val periodNanos: Long

    @Volatile
    private var worker: Thread? = null

    init {
        require(rateHz in MIN_RATE_HZ..MAX_RATE_HZ)
        periodNanos =
            1_000_000_000L / rateHz
    }

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        worker = Thread(
            { runLoop() },
            "natsx-realtime-state-publisher",
        ).apply {
            isDaemon = true
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            return
        }

        worker?.interrupt()
        worker = null
    }

    private fun runLoop() {
        var nextDeadline =
            System.nanoTime()

        while (running.get()) {
            try {
                sender.send(
                    stateStore.snapshot(),
                )
            } catch (throwable: Throwable) {
                if (running.get()) {
                    onError(throwable)
                }
                break
            }

            nextDeadline += periodNanos

            val now =
                System.nanoTime()
            val remaining =
                nextDeadline - now

            if (remaining > 0) {
                LockSupport.parkNanos(
                    remaining,
                )

                if (Thread.interrupted() &&
                    !running.get()
                ) {
                    break
                }
            } else if (
                -remaining >
                periodNanos * 4
            ) {
                // Do not replay an accumulated backlog after a scheduler
                // stall. Rebase directly onto current time.
                nextDeadline = now
            }
        }

        running.set(false)
    }

    companion object {
        const val DEFAULT_RATE_HZ = 120
        const val MIN_RATE_HZ = 30
        const val MAX_RATE_HZ = 240
    }
}
