package com.natsx.controller.core.trust

import com.natsx.controller.core.protocol.FirstPairingResult
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.transport.wifi.WifiEndpointCache

class PairingTrustManager(
    private val trustedPeerStore: TrustedPeerStore,
    private val sessionRegistry: TrustedSessionRegistry,
    private val wifiEndpointCache: WifiEndpointCache? = null,
) {
    fun persist(
        result: FirstPairingResult,
        displayName: String?,
        pairedAtEpochMillis: Long,
    ) {
        require(pairedAtEpochMillis >= 0)

        val secret =
            result.copyTrustSecret()

        try {
            trustedPeerStore.put(
                TrustedPeerRecord(
                    peerId = result.remotePeerId,
                    displayName = displayName,
                    capabilities = result.capabilities,
                    pairedAtEpochMillis =
                        pairedAtEpochMillis,
                ),
                secret,
            )
        } finally {
            secret.fill(0)
        }
    }

    fun forget(peerId: PeerId) {
        sessionRegistry.remove(peerId)
        wifiEndpointCache?.remove(peerId)
        trustedPeerStore.remove(peerId)
    }

    fun resetAll() {
        sessionRegistry.clear()

        trustedPeerStore
            .list()
            .forEach { record ->
                wifiEndpointCache?.remove(
                    record.peerId,
                )
            }

        trustedPeerStore.clear()
    }
}
