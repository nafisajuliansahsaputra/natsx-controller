package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class AuthPayloadCodecTest {
    @Test
    fun firstPairingChallengeRoundTrip() {
        val payload = AuthChallengePayload(
            mode = AuthenticationMode.FIRST_PAIRING,
            senderPeerId = PeerId.fromBytes(range(1, 16)),
            nonce = range(32, 32),
            sessionId = SessionId.fromBytes(range(64, 16)),
            ephemeralPublicKey = publicKey(96),
        )

        val encoded = AuthPayloadCodec.encodeChallenge(payload)
        val decoded = AuthPayloadCodec.decodeChallenge(encoded)

        assertEquals(AuthPayloadCodec.CHALLENGE_PAYLOAD_SIZE, encoded.size)
        assertEquals(payload.mode, decoded.mode)
        assertEquals(payload.senderPeerId, decoded.senderPeerId)
        assertArrayEquals(payload.nonce, decoded.nonce)
        assertEquals(payload.sessionId, decoded.sessionId)
        assertArrayEquals(
            payload.ephemeralPublicKey,
            decoded.ephemeralPublicKey,
        )
    }

    @Test
    fun trustedReconnectResponseRequiresZeroPublicKey() {
        val payload = AuthResponsePayload(
            mode = AuthenticationMode.TRUSTED_RECONNECT,
            senderPeerId = PeerId.fromBytes(range(1, 16)),
            nonce = range(32, 32),
            sessionId = SessionId.fromBytes(range(64, 16)),
            ephemeralPublicKey =
                ByteArray(
                    PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
                ),
            proof = range(128, 32),
        )

        val encoded = AuthPayloadCodec.encodeResponse(payload)
        val decoded = AuthPayloadCodec.decodeResponse(encoded)

        assertEquals(AuthPayloadCodec.RESPONSE_PAYLOAD_SIZE, encoded.size)
        assertEquals(payload.mode, decoded.mode)
        assertArrayEquals(payload.proof, decoded.proof)
        assertTrue(decoded.ephemeralPublicKey.all { it == 0.toByte() })
    }

    @Test
    fun trustedReconnectRejectsNonZeroPublicKeyField() {
        val publicKey =
            ByteArray(
                PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
            )
        publicKey[0] = 1

        val payload = AuthChallengePayload(
            mode = AuthenticationMode.TRUSTED_RECONNECT,
            senderPeerId = PeerId.fromBytes(range(1, 16)),
            nonce = range(32, 32),
            sessionId = SessionId.fromBytes(range(64, 16)),
            ephemeralPublicKey = publicKey,
        )

        assertThrows(IllegalArgumentException::class.java) {
            AuthPayloadCodec.encodeChallenge(payload)
        }
    }

    private fun range(
        start: Int,
        count: Int,
    ): ByteArray =
        ByteArray(count) { index ->
            (start + index).toByte()
        }

    private fun publicKey(start: Int): ByteArray =
        byteArrayOf(0x04) + range(start, 64)
}
