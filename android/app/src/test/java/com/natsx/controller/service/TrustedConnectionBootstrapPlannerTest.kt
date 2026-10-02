package com.natsx.controller.service

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.trust.TrustedPeerRecord
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class TrustedConnectionBootstrapPlannerTest {
    @Test
    fun noPersistedPeerRequiresFirstPairing() {
        assertSame(
            TrustedConnectionBootstrapPlan.PairNew,
            TrustedConnectionBootstrapPlanner
                .plan(emptyList()),
        )
    }

    @Test
    fun onePersistedPeerReconnectsAfterProcessRestart() {
        val peer =
            peerRecord(
                1,
            )

        val plan =
            TrustedConnectionBootstrapPlanner
                .plan(
                    listOf(peer),
                )

        assertEquals(
            TrustedConnectionBootstrapPlan
                .Reconnect(peer),
            plan,
        )
    }

    @Test
    fun multiplePersistedPeersNeverGuessReceiver() {
        assertSame(
            TrustedConnectionBootstrapPlan
                .RequireReceiverSelection,
            TrustedConnectionBootstrapPlanner
                .plan(
                    listOf(
                        peerRecord(1),
                        peerRecord(2),
                    ),
                ),
        )
    }

    private fun peerRecord(
        seed: Int,
    ): TrustedPeerRecord =
        TrustedPeerRecord(
            peerId =
                PeerId.fromBytes(
                    ByteArray(
                        PeerId.SIZE,
                    ) {
                        (
                            seed +
                                it
                        ).toByte()
                    },
                ),
            displayName =
                "Receiver $seed",
            capabilities =
                TransportCapabilities.WIFI or
                    TransportCapabilities.BLUETOOTH or
                    TransportCapabilities.USB_DIRECT,
            pairedAtEpochMillis =
                1_700_000_000_000L +
                    seed,
        )
}
