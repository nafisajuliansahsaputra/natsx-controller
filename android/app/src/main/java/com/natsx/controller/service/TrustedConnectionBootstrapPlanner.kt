package com.natsx.controller.service

import com.natsx.controller.core.trust.TrustedPeerRecord

sealed interface TrustedConnectionBootstrapPlan {
    data object PairNew :
        TrustedConnectionBootstrapPlan

    data class Reconnect(
        val peer: TrustedPeerRecord,
    ) : TrustedConnectionBootstrapPlan

    data object RequireReceiverSelection :
        TrustedConnectionBootstrapPlan
}

object TrustedConnectionBootstrapPlanner {
    fun plan(
        peers: List<TrustedPeerRecord>,
    ): TrustedConnectionBootstrapPlan =
        when (peers.size) {
            0 ->
                TrustedConnectionBootstrapPlan
                    .PairNew

            1 ->
                TrustedConnectionBootstrapPlan
                    .Reconnect(
                        peers.single(),
                    )

            else ->
                TrustedConnectionBootstrapPlan
                    .RequireReceiverSelection
        }
}
