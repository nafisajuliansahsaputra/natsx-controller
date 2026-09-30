package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class PairingCryptoTest {
    @Test
    fun pairingAndSessionCryptoMatchesCanonicalVector() {
        val receiver = PeerId.fromBytes(range(0x00, 16))
        val controller = PeerId.fromBytes(range(0x10, 16))
        val pairingSecret = range(0x20, 32)
        val clientNonce = range(0x40, 16)
        val serverNonce = range(0x50, 16)

        val requestProof = PairingCrypto.createPairRequestProof(
            pairingSecret,
            receiver,
            controller,
            clientNonce,
            1_790_800_000L,
        )

        val trustKey = PairingCrypto.deriveTrustKey(
            pairingSecret,
            receiver,
            controller,
            clientNonce,
            serverNonce,
        )

        val responseProof = PairingCrypto.createPairResponseProof(
            trustKey,
            receiver,
            controller,
            clientNonce,
            serverNonce,
        )

        assertEquals(
            "bb630e42793bb36ff773d9de62e8058e",
            requestProof.toHex(),
        )
        assertEquals(
            "50a4b0b28b9254932f8cfdcbac11fff9bf7621e7767f9d09d7741f1b1cf78870",
            trustKey.toHex(),
        )
        assertEquals(
            "30d0d724bcdd18fdefcdfb2de30e086a",
            responseProof.toHex(),
        )

        val sessionId = SessionId.fromBytes(range(0x60, 16))
        val authClientNonce = range(0x70, 16)
        val authServerNonce = range(0x80, 16)

        assertEquals(
            "9698761f91a8188a24a57404f52017a7",
            PairingCrypto.createAuthChallengeProof(
                trustKey,
                sessionId,
                receiver,
                controller,
                authClientNonce,
            ).toHex(),
        )

        val sessionKey = PairingCrypto.deriveSessionKey(
            trustKey,
            sessionId,
            receiver,
            controller,
            authClientNonce,
            authServerNonce,
        )

        assertEquals(
            "6137b4f1b50b2a01b5d011e2f5044927b8dd9cdb8360f6a667879e7323f0b436",
            sessionKey.toHex(),
        )
        assertEquals(
            "be8625f1f500634e4c53cc5ea7cfe004",
            PairingCrypto.createAuthResponseProof(
                trustKey,
                sessionId,
                receiver,
                controller,
                authClientNonce,
                authServerNonce,
            ).toHex(),
        )
        assertEquals(
            "f04f0d7ff1cd04b7658d7f72aa733deb",
            PairingCrypto.createSessionReadyProof(
                sessionKey,
                sessionId,
            ).toHex(),
        )
    }

    @Test
    fun fixedTimeEqualsRejectsModifiedProof() {
        val expected = range(0x00, PairingCrypto.PROOF_SIZE)
        val actual = expected.copyOf()
        actual[actual.lastIndex] = (actual.last().toInt() xor 1).toByte()

        assertFalse(PairingCrypto.fixedTimeEquals(expected, actual))
    }

    private fun range(start: Int, count: Int): ByteArray =
        ByteArray(count) { index -> (start + index).toByte() }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
