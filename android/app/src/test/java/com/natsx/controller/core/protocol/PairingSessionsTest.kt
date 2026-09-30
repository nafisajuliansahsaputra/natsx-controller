package com.natsx.controller.core.protocol

import java.security.GeneralSecurityException
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class PairingSessionsTest {
    @Test
    fun initiatorAndResponderCompleteWithSameTrustKey() {
        val initiatorPeer = PeerId.createRandom()
        val responderPeer = PeerId.createRandom()

        PairingInitiatorSession(initiatorPeer).use { initiator ->
            PairingResponderSession(
                responderPeer,
                initiator.offer,
            ).use { responder ->
                val initiatorCode =
                    initiator.acceptResponse(responder.response)

                assertEquals(
                    responder.comparisonCode,
                    initiatorCode,
                )

                val initiatorConfirm =
                    initiator.approveDisplayedCode()
                val responderConfirm =
                    responder.approveDisplayedCode()

                initiator
                    .acceptRemoteConfirmation(responderConfirm)
                    .use { initiatorMaterial ->
                        responder
                            .acceptRemoteConfirmation(initiatorConfirm)
                            .use { responderMaterial ->
                                assertEquals(
                                    responderPeer,
                                    initiatorMaterial.remotePeerId,
                                )
                                assertEquals(
                                    initiatorPeer,
                                    responderMaterial.remotePeerId,
                                )
                                assertArrayEquals(
                                    initiatorMaterial.exportTrustKey(),
                                    responderMaterial.exportTrustKey(),
                                )
                            }
                    }
            }
        }
    }

    @Test
    fun wrongRemoteConfirmationIsRejected() {
        PairingInitiatorSession(PeerId.createRandom()).use { initiator ->
            PairingResponderSession(
                PeerId.createRandom(),
                initiator.offer,
            ).use { responder ->
                initiator.acceptResponse(responder.response)
                initiator.approveDisplayedCode()

                val wrong = PairingConfirmPayload(
                    peerId = responder.remotePeerId,
                    role = PairingRole.RESPONDER,
                    proof = ByteArray(PairingConfirmPayloadCodec.PROOF_SIZE),
                )

                assertThrows(GeneralSecurityException::class.java) {
                    initiator.acceptRemoteConfirmation(wrong)
                }
            }
        }
    }

    @Test
    fun cannotCompleteBeforeLocalApproval() {
        PairingInitiatorSession(PeerId.createRandom()).use { initiator ->
            PairingResponderSession(
                PeerId.createRandom(),
                initiator.offer,
            ).use { responder ->
                initiator.acceptResponse(responder.response)
                val responderConfirm =
                    responder.approveDisplayedCode()

                assertThrows(IllegalStateException::class.java) {
                    initiator.acceptRemoteConfirmation(responderConfirm)
                }
            }
        }
    }
}
