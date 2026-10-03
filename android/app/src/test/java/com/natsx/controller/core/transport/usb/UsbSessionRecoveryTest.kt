package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateEnvelope
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class UsbSessionRecoveryTest {
    @Test
    fun networkSessionRotationInvalidatesUsbButWirelessLossDoesNot() {
        val peer = PeerId.createRandom()
        val first = SessionId.createRandom()
        TrustedSessionRegistry().use { registry ->
            registry.replace(peer, first, ByteArray(32) { 1 })
            assertTrue(UsbSessionRecovery.isCurrent(registry, peer, first))
            registry.remove(peer)
            assertTrue(UsbSessionRecovery.isCurrent(registry, peer, first))
            registry.replace(peer, SessionId.createRandom(), ByteArray(32) { 2 })
            assertFalse(UsbSessionRecovery.isCurrent(registry, peer, first))
        }
    }

    @Test
    fun senderReannouncesAuthenticatedSessionWithoutChangingStateSequence() {
        val peer = PeerId.createRandom()
        var now = 10L
        UsbTrustedSession(SessionId.createRandom(), ByteArray(32) { 3 }).use { session ->
            val output = ByteArrayOutputStream()
            val sender = UsbRealtimeSender(output, session, nowNanos = { now }, localPeerId = peer)
            try {
                for (sequence in 0u..2u) {
                    sender.publish(RealtimeStateEnvelope(sequence, sequence.toULong(), GamepadState.Neutral))
                    val deadline = System.nanoTime() + 2_000_000_000L
                    while (sender.sentFrames <= sequence.toLong() && System.nanoTime() < deadline) Thread.sleep(5)
                    assertEquals(sequence.toLong() + 1, sender.sentFrames)
                    now += if (sequence == 0u) 100_000_000L else 1_000_000_000L
                }
                val input = ByteArrayInputStream(output.toByteArray())
                assertEquals(peer, UsbControlFrameCodec.decodeSessionReady(UsbStreamFrameCodec.readFrame(input), session).peerId)
                assertEquals(0u, ProtocolFrameCodec.decode(UsbStreamFrameCodec.readFrame(input), session.authenticationKey()).sequence)
                assertEquals(1u, ProtocolFrameCodec.decode(UsbStreamFrameCodec.readFrame(input), session.authenticationKey()).sequence)
                assertEquals(peer, UsbControlFrameCodec.decodeSessionReady(UsbStreamFrameCodec.readFrame(input), session).peerId)
                assertEquals(2u, ProtocolFrameCodec.decode(UsbStreamFrameCodec.readFrame(input), session.authenticationKey()).sequence)
                assertEquals(0, input.available())
            } finally { sender.close() }
        }
    }
}
