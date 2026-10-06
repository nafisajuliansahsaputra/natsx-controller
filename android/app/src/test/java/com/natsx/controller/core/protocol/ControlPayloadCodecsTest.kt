package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Test

class ControlPayloadCodecsTest {
    @Test
    fun transportPreferenceRoundTrips() {
        TransportPreferenceMode.entries
            .forEach { mode ->
                val encoded =
                    TransportPreferencePayloadCodec
                        .encode(
                            TransportPreferencePayload(
                                mode,
                            ),
                        )

                val decoded =
                    TransportPreferencePayloadCodec
                        .decode(encoded)

                assertEquals(
                    mode,
                    decoded.mode,
                )
                assertArrayEquals(
                    byteArrayOf(
                        mode.wireValue.toByte(),
                        0,
                        0,
                        0,
                    ),
                    encoded,
                )
            }
    }

    @Test
    fun heartbeatAckRoundTripsTimestamp() {
        val expected = HeartbeatAckPayload(0x0102030405060708uL)
        val decoded = HeartbeatAckPayloadCodec.decode(
            HeartbeatAckPayloadCodec.encode(expected),
        )
        assertEquals(expected, decoded)
    }

    @Test
    fun transportReadyRoundTrips() {
        ProtocolTransport.entries.forEach { transport ->
            val encoded = TransportReadyPayloadCodec.encode(
                TransportReadyPayload(transport),
            )
            val decoded = TransportReadyPayloadCodec.decode(encoded)

            assertEquals(transport, decoded.transport)
            assertArrayEquals(
                byteArrayOf(transport.wireValue.toByte(), 0, 0, 0),
                encoded,
            )
        }
    }

    @Test
    fun handoverRoundTrips() {
        val expected = HandoverPayload(
            transport = ProtocolTransport.BLUETOOTH,
            stateSequence = 0x89abcdefu,
        )
        val decoded = HandoverPayloadCodec.decode(
            HandoverPayloadCodec.encode(expected),
        )
        assertEquals(expected, decoded)
    }

    @Test
    fun rumbleRoundTrips() {
        val expected = RumblePayload(17, 250)
        val decoded = RumblePayloadCodec.decode(
            RumblePayloadCodec.encode(expected),
        )
        assertEquals(expected, decoded)
    }

    @Test
    fun disconnectRoundTrips() {
        DisconnectReason.entries.forEach { reason ->
            val decoded = DisconnectPayloadCodec.decode(
                DisconnectPayloadCodec.encode(DisconnectPayload(reason)),
            )
            assertEquals(reason, decoded.reason)
        }
    }

    @Test
    fun reservedBytesAreRejected() {
        assertThrows(IllegalArgumentException::class.java) {
            TransportPreferencePayloadCodec.decode(
                byteArrayOf(1, 1, 0, 0),
            )
        }
        assertThrows(IllegalArgumentException::class.java) {
            TransportReadyPayloadCodec.decode(byteArrayOf(1, 1, 0, 0))
        }
        assertThrows(IllegalArgumentException::class.java) {
            HandoverPayloadCodec.decode(byteArrayOf(1, 0, 1, 0, 0, 0, 0, 0))
        }
        assertThrows(IllegalArgumentException::class.java) {
            RumblePayloadCodec.decode(byteArrayOf(1, 2, 0, 1))
        }
        assertThrows(IllegalArgumentException::class.java) {
            DisconnectPayloadCodec.decode(byteArrayOf(0, 0, 1, 0))
        }
    }
}

class TrustedReconnectCryptoTest {
    private val trustKey = hex(
        "000102030405060708090a0b0c0d0e0f" +
            "101112131415161718191a1b1c1d1e1f",
    )
    private val sessionId = SessionId.fromBytes(
        hex("00112233445566778899aabbccddeeff"),
    )
    private val challenge = hex(
        "202122232425262728292a2b2c2d2e2f" +
            "303132333435363738393a3b3c3d3e3f",
    )
    private val challenger = PeerId.fromBytes(
        hex("404142434445464748494a4b4c4d4e4f"),
    )
    private val responder = PeerId.fromBytes(
        hex("505152535455565758595a5b5c5d5e5f"),
    )

    @Test
    fun authPayloadsRoundTrip() {
        val challengePayload = AuthChallengePayload(
            challengerPeerId = challenger,
            targetPeerId = responder,
            challengeNonce = challenge,
        )
        val decodedChallenge = AuthChallengePayloadCodec.decode(
            AuthChallengePayloadCodec.encode(challengePayload),
        )

        assertArrayEquals(
            challengePayload.challengerPeerId.toByteArray(),
            decodedChallenge.challengerPeerId.toByteArray(),
        )
        assertArrayEquals(
            challengePayload.targetPeerId.toByteArray(),
            decodedChallenge.targetPeerId.toByteArray(),
        )
        assertArrayEquals(
            challengePayload.challengeNonce,
            decodedChallenge.challengeNonce,
        )

        val proof = TrustedReconnectCrypto.createProof(
            trustKey,
            sessionId,
            challenge,
            challenger,
            responder,
        )
        val responsePayload = AuthResponsePayload(
            responderPeerId = responder,
            challengerPeerId = challenger,
            proof = proof,
        )
        val decodedResponse = AuthResponsePayloadCodec.decode(
            AuthResponsePayloadCodec.encode(responsePayload),
        )

        assertArrayEquals(
            responsePayload.proof,
            decodedResponse.proof,
        )
    }

    @Test
    fun canonicalProofMatchesCSharpVector() {
        val proof = TrustedReconnectCrypto.createProof(
            trustKey,
            sessionId,
            challenge,
            challenger,
            responder,
        )

        assertEquals(
            "7c1d330d72acdce1dd6cdcb2d43d3238b45ed559b8b70c5f80126e984156fa4b",
            proof.toHex(),
        )
        assertTrue(
            TrustedReconnectCrypto.verifyProof(
                trustKey,
                sessionId,
                challenge,
                challenger,
                responder,
                proof,
            ),
        )
    }

    @Test
    fun canonicalSessionKeyMatchesCSharpVector() {
        val key = TrustedReconnectCrypto.deriveSessionKey(
            trustKey,
            sessionId,
            challenge,
            challenger,
            responder,
        )

        assertEquals(
            "7f71083bd8b16f7e75f276bb9ebb7551060bf5128c5c343b2634584f5a3200f2",
            key.toHex(),
        )
    }

    @Test
    fun proofRejectsDifferentSession() {
        val proof = TrustedReconnectCrypto.createProof(
            trustKey,
            sessionId,
            challenge,
            challenger,
            responder,
        )
        val otherSession = SessionId.fromBytes(
            hex("10112233445566778899aabbccddeeff"),
        )

        assertFalse(
            TrustedReconnectCrypto.verifyProof(
                trustKey,
                otherSession,
                challenge,
                challenger,
                responder,
                proof,
            ),
        )
    }

    private fun hex(value: String): ByteArray =
        value.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") { "%02x".format(it.toInt() and 0xFF) }
}
