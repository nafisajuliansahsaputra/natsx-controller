package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.protocol.AuthResponsePayload
import com.natsx.controller.core.protocol.AuthResponsePayloadCodec
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedReconnectCrypto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class UsbTrustedHandshakeTest {
    @Test
    fun challengeAcceptsValidTrustedResponseAndSessionReady() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val trustSecret =
            ByteArray(32) { index ->
                (index + 1).toByte()
            }

        UsbTrustedHandshakeChallenge.create(
            localPeerId = androidPeer,
            receiverPeerId = windowsPeer,
            trustSecret = trustSecret,
        ).use { challenge ->
            val challengeFrame =
                ProtocolFrameCodec.decode(
                    challenge.encodeChallenge(100uL),
                )

            val challengePayload =
                com.natsx.controller.core.protocol
                    .AuthChallengePayloadCodec
                    .decode(challengeFrame.payload)

            val proof =
                TrustedReconnectCrypto.createProof(
                    trustKey = trustSecret,
                    sessionId = challenge.sessionId,
                    challengeNonce =
                        challengePayload.challengeNonce,
                    challengerPeerId = androidPeer,
                    responderPeerId = windowsPeer,
                )

            val responseFrame =
                ProtocolFrameCodec.encode(
                    ProtocolFrame(
                        version =
                            ProtocolVersion.Current,
                        messageType =
                            MessageType.AUTH_RESPONSE,
                        flags = FrameFlags.NONE,
                        sessionId =
                            challenge.sessionId,
                        sequence = 0u,
                        monotonicTimestampMicros =
                            200uL,
                        payload =
                            AuthResponsePayloadCodec.encode(
                                AuthResponsePayload(
                                    responderPeerId =
                                        windowsPeer,
                                    challengerPeerId =
                                        androidPeer,
                                    proof = proof,
                                ),
                            ),
                    ),
                )

            challenge.acceptResponse(
                responseFrame,
            ).use { trustedSession ->
                val localReady =
                    SessionReadyPayload(
                        role =
                            PeerRole.ANDROID_CONTROLLER,
                        capabilities =
                            TransportCapabilities.WIFI or
                                TransportCapabilities.USB_DIRECT,
                        peerId = androidPeer,
                    )

                val encoded =
                    UsbControlFrameCodec
                        .encodeSessionReady(
                            trustedSession,
                            localReady,
                            300uL,
                        )

                val decoded =
                    UsbControlFrameCodec
                        .decodeSessionReady(
                            encoded,
                            trustedSession,
                        )

                assertEquals(
                    localReady,
                    decoded,
                )
                assertEquals(
                    challenge.sessionId,
                    trustedSession.sessionId,
                )
            }

            proof.fill(0)
            challengePayload.challengeNonce.fill(0)
        }

        trustSecret.fill(0)
    }

    @Test
    fun sessionReadyRequiresUsbCapability() {
        val sessionId =
            com.natsx.controller.core.protocol.SessionId
                .createRandom()
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        UsbTrustedSession(
            sessionId,
            sessionKey,
        ).use { trustedSession ->
            val payload =
                SessionReadyPayload(
                    role =
                        PeerRole.WINDOWS_RECEIVER,
                    capabilities =
                        TransportCapabilities.USB_DIRECT,
                    peerId =
                        PeerId.createRandom(),
                )

            val encoded =
                UsbControlFrameCodec
                    .encodeSessionReady(
                        trustedSession,
                        payload,
                        400uL,
                    )

            val decoded =
                UsbControlFrameCodec
                    .decodeSessionReady(
                        encoded,
                        trustedSession,
                    )

            assertTrue(
                decoded.capabilities and
                    TransportCapabilities.USB_DIRECT != 0,
            )
        }

        sessionKey.fill(0)
    }
}
