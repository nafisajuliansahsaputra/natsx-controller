package com.natsx.controller.core.session

import com.natsx.controller.core.gamepad.GamepadState
import java.util.concurrent.CopyOnWriteArrayList

class RealtimeStateBroadcaster(
    private val sequence: SessionSequence,
    private val monotonicMicros: () -> ULong,
) {
    private val sinks = CopyOnWriteArrayList<RealtimeStateSink>()

    fun addSink(sink: RealtimeStateSink) {
        sinks.addIfAbsent(sink)
    }

    fun removeSink(sink: RealtimeStateSink) {
        sinks.remove(sink)
    }

    fun hasSinks(): Boolean = sinks.isNotEmpty()

    /**
     * Creates one session-global state revision and sends that exact envelope
     * to every ready/warm transport.
     */
    fun publish(state: GamepadState): RealtimeStateEnvelope? {
        if (sinks.isEmpty()) {
            return null
        }

        val envelope = RealtimeStateEnvelope(
            sequence = sequence.next(),
            monotonicTimestampMicros = monotonicMicros(),
            state = state,
        )

        sinks.forEach { sink ->
            runCatching {
                sink.publish(envelope)
            }
        }

        return envelope
    }
}
