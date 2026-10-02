package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class PairingSessionsTest {
    @Test
    fun bothPeersProduceSameCodeAndTrustSecret() {
        val android = PeerId.createRandom()
        val windows = PeerId.createRandom()

        PairingInitiatorSession(android).use { initiator ->
            PairingResponderSession(
                windows,
                initiator.offer,
            ).use { responder ->
                val initiatorCode =
                    initiator.acceptResponse(
                        responder.response,
                    )

                assertEquals(
                    responder.comparisonCode,
                    initiatorCode,
                )

                val androidConfirm =
                    initiator.approveDisplayedCode()

                val windowsConfirm =
                    responder.approveDisplayedCode()

                initiator
                    .acceptRemoteConfirmation(
                        windowsConfirm,
                    ).use { androidMaterial ->
                        responder
                            .acceptRemoteConfirmation(
                                androidConfirm,
                            ).use { windowsMaterial ->
                                val androidSecret =
                                    androidMaterial
                                        .copyTrustSecret()

                                val windowsSecret =
                                    windowsMaterial
                                        .copyTrustSecret()

                                try {
                                    assertArrayEquals(
                                        androidSecret,
                                        windowsSecret,
                                    )
                                } finally {
                                    androidSecret.fill(0)
                                    windowsSecret.fill(0)
                                }
                            }
                    }
            }
        }
    }

    @Test(expected = java.security.GeneralSecurityException::class)
    fun wrongRoleConfirmationIsRejected() {
        val android = PeerId.createRandom()
        val windows = PeerId.createRandom()

        PairingInitiatorSession(android).use { initiator ->
            PairingResponderSession(
                windows,
                initiator.offer,
            ).use { responder ->
                initiator.acceptResponse(
                    responder.response,
                )
                initiator.approveDisplayedCode()

                val confirm =
                    responder.approveDisplayedCode()

                initiator.acceptRemoteConfirmation(
                    confirm.copy(
                        role =
                            PairingRole
                                .ANDROID_CONTROLLER,
                    ),
                )
            }
        }
    }

    @Test
    fun pairingFramesUseZeroSession() {
        val android = PeerId.createRandom()

        PairingInitiatorSession(android).use { initiator ->
            val encoded =
                PairingFrameCodec.encodeOffer(
                    initiator.offer,
                    123uL,
                )

            val decoded =
                PairingFrameCodec.decodeOffer(
                    encoded,
                )

            assertEquals(
                android,
                decoded.androidPeerId,
            )

            val frame =
                ProtocolFrameCodec.decode(
                    encoded,
                )

            assertEquals(
                MessageType.PAIRING_OFFER,
                frame.messageType,
            )
            assertEquals(
                FrameFlags.NONE,
                frame.flags,
            )
            assertEquals(
                SessionId.Zero,
                frame.sessionId,
            )
            assertEquals(
                0u,
                frame.sequence,
            )
        }
    }
}
