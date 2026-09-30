package com.natsx.controller.core.trust

import com.natsx.controller.core.protocol.PeerId

interface TrustedPeerStore {
    fun put(
        record: TrustedPeerRecord,
        trustSecret: ByteArray,
    )

    fun get(peerId: PeerId): TrustedPeerMaterial?

    fun list(): List<TrustedPeerRecord>

    fun remove(peerId: PeerId)

    fun clear()
}
