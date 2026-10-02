package com.natsx.controller.core.lifecycle

import org.junit.Assert.assertEquals
import org.junit.Test

class GameplayLifecycleSafetyTest {
    @Test
    fun focusLossNeutralizesGameplayImmediately() {
        var releases = 0
        var rumbleStops = 0

        val safety =
            GameplayLifecycleSafety(
                releaseInputs = {
                    releases += 1
                },
                stopGameRumble = {
                    rumbleStops += 1
                },
            )

        safety.onWindowFocusChanged(
            false,
        )

        assertEquals(1, releases)
        assertEquals(0, rumbleStops)

        safety.onWindowFocusChanged(
            true,
        )

        assertEquals(1, releases)
        assertEquals(0, rumbleStops)
    }

    @Test
    fun backgroundStopNeutralizesAndStopsRumble() {
        var releases = 0
        var rumbleStops = 0

        val safety =
            GameplayLifecycleSafety(
                releaseInputs = {
                    releases += 1
                },
                stopGameRumble = {
                    rumbleStops += 1
                },
            )

        safety.onStop()

        assertEquals(1, releases)
        assertEquals(1, rumbleStops)
    }
}
