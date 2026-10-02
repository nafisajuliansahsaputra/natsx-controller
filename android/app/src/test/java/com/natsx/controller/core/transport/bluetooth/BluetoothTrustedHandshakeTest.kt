package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.AuthResponsePayload
import com.natsx.controller.core.protocol.AuthResponsePayloadCodec
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TransportPreferenceMode
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.protocol.TrustedReconnectCrypto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BluetoothTrustedHandshakeTest {
    @Test
    fun challengeAcceptsValidTrustedResponseAndSessionReady() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val trustSecret =
            ByteArray(32) { index ->
                (index + 1).toByte()
            }

        BluetoothTrustedHandshakeChallenge.create(
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
                                TransportCapabilities.BLUETOOTH,
                        peerId = androidPeer,
                    )

                val encoded =
                    BluetoothControlFrameCodec
                        .encodeSessionReady(
                            trustedSession,
                            localReady,
                            300uL,
                        )

                val decoded =
                    BluetoothControlFrameCodec
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
    fun handoverCommitRoundTripPreservesAuthorityAndSequence() {
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        BluetoothTrustedSession(
            com.natsx.controller.core.protocol.SessionId
                .createRandom(),
            sessionKey,
        ).use { trustedSession ->
            val expected =
                HandoverPayload(
                    transport =
                        ProtocolTransport.USB_DIRECT,
                    stateSequence =
                        0xA1B2_C3D4u,
                )

            val encoded =
                BluetoothControlFrameCodec
                    .encodeHandoverCommit(
                        trustedSession,
                        expected,
                        500uL,
                    )

            assertEquals(
                expected,
                BluetoothControlFrameCodec
                    .decodeHandoverCommit(
                        encoded,
                        trustedSession,
                    ),
            )
        }

        sessionKey.fill(0)
    }

    @Test
    fun transportPreferenceRoundTripPreservesMode() {
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        BluetoothTrustedSession(
            com.natsx.controller.core.protocol.SessionId
                .createRandom(),
            sessionKey,
        ).use { trustedSession ->
            val expected =
                TransportPreferencePayload(
                    TransportPreferenceMode.BLUETOOTH,
                )

            val encoded =
                BluetoothControlFrameCodec
                    .encodeTransportPreference(
                        trustedSession,
                        expected,
                        510uL,
                    )

            assertEquals(
                expected,
                BluetoothControlFrameCodec
                    .decodeTransportPreference(
                        encoded,
                        trustedSession,
                    ),
            )
        }

        sessionKey.fill(0)
    }

    @Test
    fun sessionReadyRequiresBluetoothCapability() {
        val sessionId =
            com.natsx.controller.core.protocol.SessionId
                .createRandom()
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        BluetoothTrustedSession(
            sessionId,
            sessionKey,
        ).use { trustedSession ->
            val payload =
                SessionReadyPayload(
                    role =
                        PeerRole.WINDOWS_RECEIVER,
                    capabilities =
                        TransportCapabilities.BLUETOOTH,
                    peerId =
                        PeerId.createRandom(),
                )

            val encoded =
                BluetoothControlFrameCodec
                    .encodeSessionReady(
                        trustedSession,
                        payload,
                        400uL,
                    )

            val decoded =
                BluetoothControlFrameCodec
                    .decodeSessionReady(
                        encoded,
                        trustedSession,
                    )

            assertTrue(
                decoded.capabilities and
                    TransportCapabilities.BLUETOOTH != 0,
            )
        }

        sessionKey.fill(0)
    }
}
