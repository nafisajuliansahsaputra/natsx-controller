package com.natsx.controller.core.session

import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class RealtimeStateBroadcasterTest {
    @Test
    fun sameGlobalEnvelopeIsDeliveredToEveryWarmTransport() {
        val sequence = SessionSequence(initialValue = 100u)
        var now = 50_000uL
        val broadcaster = RealtimeStateBroadcaster(sequence) { now }

        val wifi = mutableListOf<RealtimeStateEnvelope>()
        val bluetooth = mutableListOf<RealtimeStateEnvelope>()

        broadcaster.addSink(RealtimeStateSink { wifi += it })
        broadcaster.addSink(RealtimeStateSink { bluetooth += it })

        val state = GamepadState.Neutral.copy(
            buttons = GamepadButtons.A,
            leftX = 1234,
        )

        val envelope = broadcaster.publish(state)

        assertEquals(100u, envelope?.sequence)
        assertEquals(now, envelope?.monotonicTimestampMicros)
        assertEquals(state, envelope?.state)
        assertEquals(envelope, wifi.single())
        assertEquals(envelope, bluetooth.single())
        assertEquals(101u, sequence.peek())
    }

    @Test
    fun noSinkDoesNotConsumeGlobalSequence() {
        val sequence = SessionSequence(initialValue = 7u)
        val broadcaster = RealtimeStateBroadcaster(sequence) { 1uL }

        assertNull(broadcaster.publish(GamepadState.Neutral))
        assertEquals(7u, sequence.peek())
    }

    @Test
    fun oneFailingSinkDoesNotBlockOtherTransport() {
        val sequence = SessionSequence()
        val broadcaster = RealtimeStateBroadcaster(sequence) { 1uL }
        val received = mutableListOf<RealtimeStateEnvelope>()

        broadcaster.addSink(RealtimeStateSink {
            error("transport failed")
        })
        broadcaster.addSink(RealtimeStateSink { received += it })

        broadcaster.publish(GamepadState.Neutral)

        assertEquals(1, received.size)
    }
}
