package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.protocol.*
import com.natsx.controller.core.trust.*
import org.junit.Assert.*
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class WifiFirstPairingClientTest {
    @Test
    fun repairsStaleTrustAfterBothApprovalsAndWaitsBeyondDiscoveryTimeout() {
        exchange(approved = true) { result, store, oldKey, newKey ->
            assertNotNull(result)
            assertArrayEquals(newKey, store.key)
            assertFalse(oldKey.contentEquals(store.key))
        }
    }

    @Test
    fun rejectionSendsAbortAndRetainsOldTrust() {
        exchange(approved = false) { result, store, oldKey, _ ->
            assertNull(result)
            assertArrayEquals(oldKey, store.key)
        }
    }

    private fun exchange(
        approved: Boolean,
        verify: (TrustedPeerRecord?, MemoryStore, ByteArray, ByteArray) -> Unit,
    ) {
        val phonePeer = PeerId.createRandom()
        val pcPeer = PeerId.createRandom()
        val store = MemoryStore()
        val oldKey = ByteArray(32) { 42 }
        store.put(TrustedPeerRecord(pcPeer, "PC", TransportCapabilities.WIFI, 0), oldKey)
        val confirmation = PairingConfirmationCoordinator()
        confirmation.addListener { if (it != null) confirmation.resolve(approved) }
        val executor = Executors.newSingleThreadExecutor()
        try {
            DatagramSocket(InetSocketAddress(InetAddress.getLoopbackAddress(), WifiFirstPairingClient.PAIRING_PORT)).use { receiver ->
                receiver.soTimeout = 5_000
                val server = executor.submit<ByteArray> {
                    val offerPacket = receive(receiver)
                    val offer = PairingFrameCodec.decodeOffer(bytes(offerPacket))
                    PairingResponderSession(pcPeer, offer).use { responder ->
                        send(receiver, PairingFrameCodec.encodeResponse(responder.response), offerPacket)
                        val confirm = receive(receiver)
                        if (!approved) {
                            assertEquals(MessageType.PAIRING_ABORT, ProtocolFrameCodec.decode(bytes(confirm)).messageType)
                            return@submit ByteArray(0)
                        }
                        val local = responder.approveDisplayedCode()
                        responder.acceptRemoteConfirmation(PairingFrameCodec.decodeConfirm(bytes(confirm))).use { material ->
                            // Local approval may precede Windows approval by longer than the discovery deadline.
                            Thread.sleep(750)
                            send(receiver, PairingFrameCodec.encodeConfirm(local), offerPacket)
                            material.copyTrustSecret()
                        }
                    }
                }
                val result = runCatching {
                    WifiFirstPairingClient(phonePeer, store, confirmation, timeoutMillis = 500)
                        .pair(InetSocketAddress(InetAddress.getLoopbackAddress(), 43860), pcPeer)
                }
                val newKey = server.get(5, TimeUnit.SECONDS)
                if (approved) assertTrue(result.exceptionOrNull()?.toString(), result.isSuccess)
                else assertTrue(result.isFailure)
                verify(result.getOrNull(), store, oldKey, newKey)
            }
        } finally {
            executor.shutdownNow()
        }
    }

    private fun receive(socket: DatagramSocket): DatagramPacket =
        DatagramPacket(ByteArray(512), 512).also(socket::receive)

    private fun bytes(packet: DatagramPacket): ByteArray =
        packet.data.copyOfRange(packet.offset, packet.offset + packet.length)

    private fun send(socket: DatagramSocket, data: ByteArray, target: DatagramPacket) {
        socket.send(DatagramPacket(data, data.size, target.socketAddress))
    }

    private class MemoryStore : TrustedPeerStore {
        private var record: TrustedPeerRecord? = null
        var key = ByteArray(0)
            private set
        override fun put(record: TrustedPeerRecord, trustSecret: ByteArray) {
            this.record = record
            key = trustSecret.copyOf()
        }
        override fun get(peerId: PeerId): TrustedPeerMaterial? =
            record?.takeIf { it.peerId == peerId }?.let { TrustedPeerMaterial(it, key) }
        override fun list(): List<TrustedPeerRecord> = listOfNotNull(record)
        override fun remove(peerId: PeerId) { if (record?.peerId == peerId) clear() }
        override fun clear() { record = null; key.fill(0); key = ByteArray(0) }
    }
}
