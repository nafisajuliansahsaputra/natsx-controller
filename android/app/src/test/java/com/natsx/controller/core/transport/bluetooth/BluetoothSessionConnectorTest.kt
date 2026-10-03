package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.*
import com.natsx.controller.core.trust.*
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream

class BluetoothSessionConnectorTest {
    @Test
    fun coldReconnectAuthenticatesWithoutWifiAndRemovesOnlyItsOwnSession() {
        val phone = PeerId.createRandom()
        val pc = PeerId.createRandom()
        val secret = ByteArray(32) { (it + 1).toByte() }
        val record = TrustedPeerRecord(pc, "PC", TransportCapabilities.BLUETOOTH, 0)
        val store = object : TrustedPeerStore {
            override fun get(peerId: PeerId) = if (peerId == pc) TrustedPeerMaterial(record, secret) else null
            override fun list() = listOf(record)
            override fun put(record: TrustedPeerRecord, trustSecret: ByteArray) = error("No pairing expected")
            override fun remove(peerId: PeerId) = error("No trust removal expected")
            override fun clear() = error("No trust removal expected")
        }
        TrustedSessionRegistry().use { registry ->
            val output = ByteArrayOutputStream()
            val input = object : InputStream() {
                private var response: ByteArrayInputStream? = null
                override fun read(): Int {
                    if (response == null) {
                        val frame = ProtocolFrameCodec.decode(BluetoothStreamFrameCodec.readFrame(ByteArrayInputStream(output.toByteArray())))
                        assertEquals(MessageType.AUTH_CHALLENGE, frame.messageType)
                        val challenge = AuthChallengePayloadCodec.decode(frame.payload)
                        val proof = TrustedReconnectCrypto.createProof(secret, frame.sessionId, challenge.challengeNonce, phone, pc)
                        val auth = ProtocolFrameCodec.encode(ProtocolFrame(
                            ProtocolVersion.Current, MessageType.AUTH_RESPONSE, FrameFlags.NONE,
                            frame.sessionId, 0u, 200uL,
                            AuthResponsePayloadCodec.encode(AuthResponsePayload(pc, phone, proof)),
                        ))
                        val key = TrustedReconnectCrypto.deriveSessionKey(secret, frame.sessionId, challenge.challengeNonce, phone, pc)
                        BluetoothTrustedSession(frame.sessionId, key).use { session ->
                            val ready = BluetoothControlFrameCodec.encodeSessionReady(session,
                                SessionReadyPayload(PeerRole.WINDOWS_RECEIVER, TransportCapabilities.BLUETOOTH, pc), 300uL)
                            response = ByteArrayInputStream(BluetoothStreamFrameCodec.encode(auth) + BluetoothStreamFrameCodec.encode(ready))
                        }
                        key.fill(0)
                    }
                    return response!!.read()
                }
            }
            val connector = BluetoothSessionConnector(phone, pc, registry, store, { 100uL })
            connector.connect(input, output).use { session ->
                registry.get(pc)!!.use { assertEquals(session.sessionId, it.sessionId) }
                // A late close must not revoke a newer Wi-Fi session.
                val newer = SessionId.createRandom()
                registry.replace(pc, newer, ByteArray(32) { 77 })
                connector.sessionClosed()
                registry.get(pc)!!.use { assertEquals(newer, it.sessionId) }
            }
        }
    }

    @Test(expected = IllegalStateException::class)
    fun unpairedReceiverCannotUseColdBluetoothReconnect() {
        TrustedSessionRegistry().use { registry ->
            BluetoothSessionConnector(PeerId.createRandom(), PeerId.createRandom(), registry, null, { 1uL })
                .connect(ByteArrayInputStream(byteArrayOf()), ByteArrayOutputStream())
        }
    }
}
