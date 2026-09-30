package com.natsx.controller.core.gamepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GamepadStateStoreTest {
    @Test
    fun independentButtonsCanBePressedTogether() {
        val store = GamepadStateStore()

        store.setButton(GamepadButtons.A, true)
        store.setButton(GamepadButtons.RIGHT_SHOULDER, true)
        store.setButton(GamepadButtons.X, true)

        val state = store.snapshot()

        assertTrue(state.buttons and GamepadButtons.A != 0)
        assertTrue(state.buttons and GamepadButtons.RIGHT_SHOULDER != 0)
        assertTrue(state.buttons and GamepadButtons.X != 0)
    }

    @Test
    fun releasingOneButtonDoesNotReleaseOthers() {
        val store = GamepadStateStore()

        store.setButton(GamepadButtons.A, true)
        store.setButton(GamepadButtons.B, true)
        store.setButton(GamepadButtons.A, false)

        assertEquals(GamepadButtons.B, store.snapshot().buttons)
    }

    @Test
    fun opposingDpadDirectionsResolveToNeutralAxis() {
        val store = GamepadStateStore()

        store.setDpad(DpadState.UP, true)
        store.setDpad(DpadState.DOWN, true)

        assertEquals(DpadState.NEUTRAL, store.snapshot().dpad)
    }

    @Test
    fun neutralizeClearsEveryInput() {
        val store = GamepadStateStore()

        store.setButton(GamepadButtons.A, true)
        store.setLeftStick(12000, -14000)
        store.setRightTrigger(255)
        store.setDpad(DpadState.LEFT, true)

        store.neutralize()

        assertEquals(GamepadState.Neutral, store.snapshot())
    }
}
