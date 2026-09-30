package com.natsx.controller.core.trust

import com.natsx.controller.core.protocol.PeerId

data class TrustedPeerRecord(
    val peerId: PeerId,
    val displayName: String?,
    val capabilities: Int,
    val pairedAtEpochMillis: Long,
    val pairingVersion: Int = CURRENT_PAIRING_VERSION,
) {
    init {
        require(pairedAtEpochMillis >= 0)
        require(pairingVersion > 0)
    }

    companion object {
        const val CURRENT_PAIRING_VERSION = 1
    }
}

class TrustedPeerMaterial(
    val record: TrustedPeerRecord,
    trustSecret: ByteArray,
) : AutoCloseable {
    private val secret = trustSecret.copyOf()
    private var closed = false

    init {
        require(secret.size == TRUST_SECRET_SIZE) {
            "Trust secret must be exactly 32 bytes."
        }
    }

    fun copyTrustSecret(): ByteArray {
        check(!closed) { "Trusted peer material is closed." }
        return secret.copyOf()
    }

    override fun close() {
        if (closed) return
        secret.fill(0)
        closed = true
    }

    companion object {
        const val TRUST_SECRET_SIZE = 32
    }
}
