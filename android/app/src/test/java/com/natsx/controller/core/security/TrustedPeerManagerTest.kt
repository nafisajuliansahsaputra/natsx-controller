package com.natsx.controller.core.security

import com.natsx.controller.core.protocol.PairingEstablishedMaterial
import com.natsx.controller.core.protocol.PeerId
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TrustedPeerManagerTest {
    @Test
    fun persistEstablishedPairingStoresRemoteTrustKey() {
        val store = MemoryStore()
        val manager = TrustedPeerManager(store)
        val peerId = PeerId.createRandom()
        val trustKey = ByteArray(32) { (it + 1).toByte() }

        PairingEstablishedMaterial(
            remotePeerId = peerId,
            trustKey = trustKey,
        ).use { material ->
            manager.persistEstablishedPairing(material)
        }

        assertTrue(manager.isTrusted(peerId))
        assertArrayEquals(
            trustKey,
            store.getTrustKey(peerId),
        )
    }

    @Test
    fun forgetRemovesTrustRelationship() {
        val store = MemoryStore()
        val manager = TrustedPeerManager(store)
        val peerId = PeerId.createRandom()
        val trustKey = ByteArray(32) { it.toByte() }

        store.saveTrustKey(peerId, trustKey)

        assertTrue(manager.forget(peerId))
        assertFalse(manager.isTrusted(peerId))
    }

    private class MemoryStore : TrustedPeerStore {
        private val keys = mutableMapOf<PeerId, ByteArray>()

        override fun saveTrustKey(
            peerId: PeerId,
            trustKey: ByteArray,
        ) {
            keys[peerId] = trustKey.copyOf()
        }

        override fun getTrustKey(peerId: PeerId): ByteArray? =
            keys[peerId]?.copyOf()

        override fun contains(peerId: PeerId): Boolean =
            keys.containsKey(peerId)

        override fun remove(peerId: PeerId): Boolean {
            val removed = keys.remove(peerId) ?: return false
            removed.fill(0)
            return true
        }
    }
}
