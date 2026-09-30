package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PairingProtocolTest {
    private val offer = PairingOfferPayload(
        initiatorPeerId = PeerId.fromBytes(
            hex("101112131415161718191a1b1c1d1e1f"),
        ),
        initiatorNonce = hex(
            "202122232425262728292a2b2c2d2e2f" +
                "303132333435363738393a3b3c3d3e3f",
        ),
        initiatorPublicKey = hex(
            "04" +
                "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f" +
                "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f",
        ),
    )

    private val response = PairingResponsePayload(
        responderPeerId = PeerId.fromBytes(
            hex("808182838485868788898a8b8c8d8e8f"),
        ),
        responderNonce = hex(
            "909192939495969798999a9b9c9d9e9f" +
                "a0a1a2a3a4a5a6a7a8a9aaabacadaeaf",
        ),
        responderPublicKey = hex(
            "04" +
                "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecf" +
                "d0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeef",
        ),
    )

    private val sharedSecret = ByteArray(32) { (it + 1).toByte() }

    @Test
    fun offerAndResponseRoundTrip() {
        val decodedOffer = PairingExchangePayloadCodec.decodeOffer(
            PairingExchangePayloadCodec.encodeOffer(offer),
        )
        val decodedResponse = PairingExchangePayloadCodec.decodeResponse(
            PairingExchangePayloadCodec.encodeResponse(response),
        )

        assertArrayEquals(
            offer.initiatorPeerId.toByteArray(),
            decodedOffer.initiatorPeerId.toByteArray(),
        )
        assertArrayEquals(offer.initiatorNonce, decodedOffer.initiatorNonce)
        assertArrayEquals(offer.initiatorPublicKey, decodedOffer.initiatorPublicKey)

        assertArrayEquals(
            response.responderPeerId.toByteArray(),
            decodedResponse.responderPeerId.toByteArray(),
        )
        assertArrayEquals(response.responderNonce, decodedResponse.responderNonce)
        assertArrayEquals(response.responderPublicKey, decodedResponse.responderPublicKey)
    }

    @Test
    fun canonicalPairingDerivationMatchesCSharpVector() {
        val transcriptHash = PairingCrypto.computeTranscriptHash(offer, response)
        val trustKey = PairingCrypto.deriveTrustKey(sharedSecret, transcriptHash)
        val code = PairingCrypto.deriveSixDigitCode(sharedSecret, transcriptHash)

        assertEquals(
            "70fcc403a3a3d7525d16359e622f44caf69d99b62ff77429cc465d776b83aece",
            transcriptHash.toHex(),
        )
        assertEquals(
            "0d7312c8a793c72f4f59d0c48aba5ee6e749dbe6f440e0e6eba1e711fa74b1ec",
            trustKey.toHex(),
        )
        assertEquals("694336", code)

        val initiatorProof = PairingCrypto.createConfirmationProof(
            trustKey,
            transcriptHash,
            PairingRole.INITIATOR,
        )
        val responderProof = PairingCrypto.createConfirmationProof(
            trustKey,
            transcriptHash,
            PairingRole.RESPONDER,
        )

        assertEquals(
            "1c875bd84d8dcb3c002d4e2ef206fb122977ab9eaf2924cf67627ee13b5beda9",
            initiatorProof.toHex(),
        )
        assertEquals(
            "77aa59d281e0af944592dfd35a60e801459f20508f27768a4ffed97858324562",
            responderProof.toHex(),
        )
        assertTrue(
            PairingCrypto.verifyConfirmationProof(
                trustKey,
                transcriptHash,
                PairingRole.RESPONDER,
                responderProof,
            ),
        )
    }

    @Test
    fun ecdhAgreementIsSymmetric() {
        PairingKeyAgreement().use { left ->
            PairingKeyAgreement().use { right ->
                val leftSecret = left.deriveSharedSecret(right.publicKey)
                val rightSecret = right.deriveSharedSecret(left.publicKey)

                assertEquals(PairingCrypto.SHARED_SECRET_SIZE, leftSecret.size)
                assertArrayEquals(leftSecret, rightSecret)
            }
        }
    }

    @Test
    fun confirmPayloadRoundTrips() {
        val proof = ByteArray(32) { it.toByte() }
        val expected = PairingConfirmPayload(
            peerId = offer.initiatorPeerId,
            role = PairingRole.INITIATOR,
            proof = proof,
        )

        val decoded = PairingConfirmPayloadCodec.decode(
            PairingConfirmPayloadCodec.encode(expected),
        )

        assertEquals(expected.role, decoded.role)
        assertArrayEquals(expected.peerId.toByteArray(), decoded.peerId.toByteArray())
        assertArrayEquals(expected.proof, decoded.proof)
    }

    private fun hex(value: String): ByteArray =
        value.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") { "%02x".format(it.toInt() and 0xFF) }
}
