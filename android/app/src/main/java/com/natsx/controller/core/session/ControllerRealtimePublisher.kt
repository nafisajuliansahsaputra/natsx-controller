package com.natsx.controller.core.session

import android.os.SystemClock
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateListener
import com.natsx.controller.core.gamepad.GamepadStateStore
import java.io.Closeable
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class ControllerRealtimePublisher(
    private val stateStore: GamepadStateStore,
    private val broadcaster: RealtimeStateBroadcaster,
    private val inputRateHz: Int = DEFAULT_INPUT_RATE_HZ,
) : Closeable, GamepadStateListener {
    private val executor: ScheduledExecutorService =
        Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(runnable, "natsx-realtime-publisher").apply {
                isDaemon = true
                priority = Thread.NORM_PRIORITY + 1
            }
        }

    private val started = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)
    private val immediateScheduled = AtomicBoolean(false)
    private val latestChangedState = AtomicReference<GamepadState?>(null)

    @Volatile
    private var periodicTask: ScheduledFuture<*>? = null

    init {
        require(inputRateHz in MIN_INPUT_RATE_HZ..MAX_INPUT_RATE_HZ) {
            "Input rate must be between $MIN_INPUT_RATE_HZ and $MAX_INPUT_RATE_HZ Hz."
        }
    }

    fun start() {
        check(!closed.get()) { "Realtime publisher is closed." }

        if (!started.compareAndSet(false, true)) {
            return
        }

        stateStore.addListener(this)

        val periodNanos = 1_000_000_000L / inputRateHz
        periodicTask = executor.scheduleAtFixedRate(
            ::publishPeriodic,
            0,
            periodNanos,
            TimeUnit.NANOSECONDS,
        )
    }

    fun addSink(sink: RealtimeStateSink) {
        broadcaster.addSink(sink)
        requestImmediate(stateStore.snapshot())
    }

    fun removeSink(sink: RealtimeStateSink) {
        broadcaster.removeSink(sink)
    }

    override fun onGamepadStateChanged(state: GamepadState) {
        if (!started.get() || closed.get()) {
            return
        }

        requestImmediate(state)
    }

    private fun requestImmediate(state: GamepadState) {
        latestChangedState.set(state)

        if (immediateScheduled.compareAndSet(false, true)) {
            executor.execute(::drainImmediate)
        }
    }

    private fun drainImmediate() {
        while (!closed.get()) {
            val state = latestChangedState.getAndSet(null)

            if (state != null) {
                broadcaster.publish(state)
            }

            immediateScheduled.set(false)

            if (
                latestChangedState.get() != null &&
                immediateScheduled.compareAndSet(false, true)
            ) {
                continue
            }

            return
        }

        immediateScheduled.set(false)
    }

    private fun publishPeriodic() {
        if (!started.get() || closed.get() || !broadcaster.hasSinks()) {
            return
        }

        broadcaster.publish(stateStore.snapshot())
    }

    fun stop() {
        if (!started.compareAndSet(true, false)) {
            return
        }

        stateStore.removeListener(this)
        periodicTask?.cancel(false)
        periodicTask = null
        latestChangedState.set(null)
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        if (started.getAndSet(false)) {
            stateStore.removeListener(this)
        }

        periodicTask?.cancel(false)
        periodicTask = null
        latestChangedState.set(null)
        executor.shutdownNow()
    }

    companion object {
        const val DEFAULT_INPUT_RATE_HZ = 120
        const val MIN_INPUT_RATE_HZ = 30
        const val MAX_INPUT_RATE_HZ = 240

        fun defaultMonotonicMicros(): ULong =
            SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL
    }
}
