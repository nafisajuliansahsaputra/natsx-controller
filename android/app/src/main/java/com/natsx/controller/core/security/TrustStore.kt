package com.natsx.controller.core.security

import com.natsx.controller.core.protocol.PeerId

interface TrustedPeerStore {
    fun saveTrustKey(
        peerId: PeerId,
        trustKey: ByteArray,
    )

    fun getTrustKey(peerId: PeerId): ByteArray?

    fun contains(peerId: PeerId): Boolean

    fun remove(peerId: PeerId): Boolean
}

interface LocalPeerIdentityStore {
    fun getOrCreate(): PeerId
}
