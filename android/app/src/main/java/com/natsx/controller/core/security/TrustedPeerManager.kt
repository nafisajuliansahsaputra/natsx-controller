package com.natsx.controller.core.security

import com.natsx.controller.core.protocol.PairingEstablishedMaterial
import com.natsx.controller.core.protocol.PeerId

class TrustedPeerManager(
    private val store: TrustedPeerStore,
) {
    fun persistEstablishedPairing(
        material: PairingEstablishedMaterial,
    ) {
        val trustKey = material.exportTrustKey()

        try {
            store.saveTrustKey(
                peerId = material.remotePeerId,
                trustKey = trustKey,
            )
        } finally {
            trustKey.fill(0)
        }
    }

    fun forget(peerId: PeerId): Boolean =
        store.remove(peerId)

    fun isTrusted(peerId: PeerId): Boolean =
        store.contains(peerId)
}
