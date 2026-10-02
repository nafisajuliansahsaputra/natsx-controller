package com.natsx.controller.core.connection

import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.ProtocolTransport
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
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

    @Test
    fun handoverCommitRejectsDuplicateAndOlderStateSequences() {
        val coordinator =
            AndroidConnectionStatusCoordinator()

        assertTrue(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport = ProtocolTransport.WIFI,
                    stateSequence = 100u,
                ),
            ),
        )

        assertFalse(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport =
                        ProtocolTransport.BLUETOOTH,
                    stateSequence = 100u,
                ),
            ),
        )

        assertFalse(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport =
                        ProtocolTransport.BLUETOOTH,
                    stateSequence = 99u,
                ),
            ),
        )

        assertTrue(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport =
                        ProtocolTransport.USB_DIRECT,
                    stateSequence = 101u,
                ),
            ),
        )

        assertEquals(
            ProtocolTransport.USB_DIRECT,
            coordinator.current()
                .smartAutoActiveTransport,
        )
        assertEquals(
            101u,
            coordinator.current()
                .smartAutoStateSequence,
        )
    }

    @Test
    fun handoverCommitAcceptsUnsignedSequenceWraparound() {
        val coordinator =
            AndroidConnectionStatusCoordinator()

        assertTrue(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport = ProtocolTransport.WIFI,
                    stateSequence = UInt.MAX_VALUE,
                ),
            ),
        )
        assertTrue(
            coordinator.applyHandoverCommit(
                HandoverPayload(
                    transport =
                        ProtocolTransport.BLUETOOTH,
                    stateSequence = 0u,
                ),
            ),
        )

        assertEquals(
            ProtocolTransport.BLUETOOTH,
            coordinator.current()
                .smartAutoActiveTransport,
        )
        assertEquals(
            0u,
            coordinator.current()
                .smartAutoStateSequence,
        )
    }

    @Test
    fun localLinkRefreshPreservesWindowsAuthority() {
        val coordinator =
            AndroidConnectionStatusCoordinator()

        coordinator.applyHandoverCommit(
            HandoverPayload(
                transport =
                    ProtocolTransport.USB_DIRECT,
                stateSequence = 77u,
            ),
        )

        coordinator.publishLinks(
            AndroidConnectionStatus(
                wifi = AndroidLinkState.ACTIVE,
                bluetooth = AndroidLinkState.ACTIVE,
                usb = AndroidLinkState.IDLE,
                trustedPcCount = 1,
                wifiReconnectAttempts = 4,
                bluetoothReconnectAttempts = 2,
            ),
        )

        val current =
            coordinator.current()

        assertEquals(
            ProtocolTransport.USB_DIRECT,
            current.smartAutoActiveTransport,
        )
        assertEquals(
            77u,
            current.smartAutoStateSequence,
        )
        assertEquals(
            AndroidLinkState.IDLE,
            current.usb,
        )
    }
}
