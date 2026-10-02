package com.natsx.controller.core.connection

import org.junit.Assert.assertEquals
import org.junit.Test

class AndroidConnectionStatusCoordinatorTest {
    @Test
    fun listenerReceivesCurrentAndOnlyMeaningfulChanges() {
        val coordinator =
            AndroidConnectionStatusCoordinator()
        val observed =
            mutableListOf<AndroidConnectionStatus>()

        val listener:
            (AndroidConnectionStatus) -> Unit = {
                observed += it
            }

        coordinator.addListener(listener)

        val active =
            AndroidConnectionStatus(
                wifi = AndroidLinkState.ACTIVE,
                bluetooth =
                    AndroidLinkState.RECONNECTING,
                usb = AndroidLinkState.ACTIVE,
                trustedPcCount = 1,
                wifiReconnectAttempts = 2,
                bluetoothReconnectAttempts = 3,
            )

        coordinator.publish(active)
        coordinator.publish(active)
        coordinator.removeListener(listener)
        coordinator.publish(
            active.copy(
                usb = AndroidLinkState.IDLE,
            ),
        )

        assertEquals(
            listOf(
                AndroidConnectionStatus(),
                active,
            ),
            observed,
        )
    }
}
