package com.natsx.controller.core.gamepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GamepadStateListenerTest {
    @Test
    fun listenerReceivesLatestStateAndNeutralization() {
        val store = GamepadStateStore()
        val states = mutableListOf<GamepadState>()

        val listener = GamepadStateListener { state ->
            states += state
        }

        store.addListener(listener)
        store.setButton(GamepadButtons.A, true)
        store.setLeftTrigger(200)
        store.neutralize()
        store.removeListener(listener)

        assertEquals(3, states.size)
        assertTrue(states[0].buttons and GamepadButtons.A != 0)
        assertEquals(200, states[1].leftTrigger)
        assertEquals(GamepadState.Neutral, states[2])
    }

    @Test
    fun unchangedStateDoesNotRepublish() {
        val store = GamepadStateStore()
        var count = 0

        store.addListener(GamepadStateListener { count++ })
        store.setLeftTrigger(0)
        store.neutralize()

        assertEquals(0, count)
    }
}
