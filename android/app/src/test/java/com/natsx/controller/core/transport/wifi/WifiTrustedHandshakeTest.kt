package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import java.security.GeneralSecurityException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class WifiTrustedHandshakeTest {
    @Test
    fun trustedReconnectDerivesInteroperableSession() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val trustSecret = ByteArray(32) { (it + 1).toByte() }

        WifiTrustedHandshakeChallenge.create(
            localPeerId = androidPeer,
            receiverPeerId = windowsPeer,
            trustSecret = trustSecret,
        ).use { challenger ->
            val responder = WifiTrustedHandshakeResponder(windowsPeer)

            responder.handleChallenge(
                datagram = challenger.encodeChallenge(100_000uL),
                trustSecret = trustSecret,
                monotonicTimestampMicros = 101_000uL,
            ).use { response ->
                assertEquals(androidPeer, response.remotePeerId)

                challenger.acceptResponse(
                    response.responseDatagram,
                ).use { challengerSession ->
                    assertEquals(
                        challenger.sessionId,
                        response.session.sessionId,
                    )
                    assertEquals(
                        challenger.sessionId,
                        challengerSession.sessionId,
                    )

                    val ready = SessionReadyPayload(
                        role = PeerRole.ANDROID_CONTROLLER,
                        capabilities = TransportCapabilities.WIFI,
                        peerId = androidPeer,
                    )

                    val authenticated =
                        WifiControlDatagramCodec.encodeSessionReady(
                            trustedSession = challengerSession,
                            payload = ready,
                            monotonicTimestampMicros = 102_000uL,
                        )

                    val decoded =
                        WifiControlDatagramCodec.decodeSessionReady(
                            authenticated,
                            response.session,
                        )

                    assertEquals(ready.role, decoded.role)
                    assertEquals(ready.capabilities, decoded.capabilities)
                    assertEquals(ready.peerId, decoded.peerId)
                }
            }
        }
    }

    @Test
    fun responseFromWrongTrustSecretIsRejected() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val expectedSecret = ByteArray(32) { (it + 1).toByte() }
        val wrongSecret = ByteArray(32) { (it + 41).toByte() }

        WifiTrustedHandshakeChallenge.create(
            androidPeer,
            windowsPeer,
            expectedSecret,
        ).use { challenger ->
            val responder = WifiTrustedHandshakeResponder(windowsPeer)

            responder.handleChallenge(
                challenger.encodeChallenge(100uL),
                wrongSecret,
                200uL,
            ).use { response ->
                assertThrows(GeneralSecurityException::class.java) {
                    challenger.acceptResponse(response.responseDatagram)
                }
            }
        }
    }

    @Test
    fun challengeForDifferentReceiverIsRejected() {
        val androidPeer = PeerId.createRandom()
        val intendedReceiver = PeerId.createRandom()
        val actualReceiver = PeerId.createRandom()
        val trustSecret = ByteArray(32) { (it + 1).toByte() }

        WifiTrustedHandshakeChallenge.create(
            androidPeer,
            intendedReceiver,
            trustSecret,
        ).use { challenger ->
            val responder = WifiTrustedHandshakeResponder(actualReceiver)

            assertThrows(GeneralSecurityException::class.java) {
                responder.handleChallenge(
                    challenger.encodeChallenge(100uL),
                    trustSecret,
                    200uL,
                )
            }
        }
    }
}
