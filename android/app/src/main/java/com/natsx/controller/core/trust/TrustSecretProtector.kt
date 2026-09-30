package com.natsx.controller.core.trust

import com.natsx.controller.core.protocol.PeerId

data class ProtectedTrustSecret(
    val iv: ByteArray,
    val ciphertext: ByteArray,
) {
    init {
        require(iv.isNotEmpty())
        require(ciphertext.isNotEmpty())
    }
}

interface TrustSecretProtector {
    fun protect(
        peerId: PeerId,
        plaintext: ByteArray,
    ): ProtectedTrustSecret

    fun unprotect(
        peerId: PeerId,
        protectedSecret: ProtectedTrustSecret,
    ): ByteArray
}
