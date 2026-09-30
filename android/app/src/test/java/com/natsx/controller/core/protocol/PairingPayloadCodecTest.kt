package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class PairingPayloadCodecTest {
    @Test
    fun pairRequestRoundTrips() {
        val payload = PairRequestPayload(
            controllerPeerId = PeerId.fromBytes(range(0x10, 16)),
            clientNonce = range(0x20, 16),
            expiresUnixSeconds = 1_790_800_000L,
            proof = range(0x30, 16),
        )

        val decoded = PairingPayloadCodec.decodePairRequest(
            PairingPayloadCodec.encodePairRequest(payload),
        )

        assertEquals(payload.controllerPeerId, decoded.controllerPeerId)
        assertArrayEquals(payload.clientNonce, decoded.clientNonce)
        assertEquals(payload.expiresUnixSeconds, decoded.expiresUnixSeconds)
        assertArrayEquals(payload.proof, decoded.proof)
    }

    @Test
    fun authPayloadsRoundTrip() {
        val challenge = AuthChallengePayload(
            controllerPeerId = PeerId.fromBytes(range(0x00, 16)),
            clientNonce = range(0x10, 16),
            proof = range(0x20, 16),
        )

        val decodedChallenge = PairingPayloadCodec.decodeAuthChallenge(
            PairingPayloadCodec.encodeAuthChallenge(challenge),
        )

        assertEquals(challenge.controllerPeerId, decodedChallenge.controllerPeerId)
        assertArrayEquals(challenge.clientNonce, decodedChallenge.clientNonce)
        assertArrayEquals(challenge.proof, decodedChallenge.proof)

        val response = AuthResponsePayload(
            serverNonce = range(0x30, 16),
            proof = range(0x40, 16),
        )

        val decodedResponse = PairingPayloadCodec.decodeAuthResponse(
            PairingPayloadCodec.encodeAuthResponse(response),
        )

        assertArrayEquals(response.serverNonce, decodedResponse.serverNonce)
        assertArrayEquals(response.proof, decodedResponse.proof)
    }

    private fun range(start: Int, count: Int): ByteArray =
        ByteArray(count) { index -> (start + index).toByte() }
}
