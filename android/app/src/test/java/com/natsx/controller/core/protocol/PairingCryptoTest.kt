package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class PairingCryptoTest {
    @Test
    fun canonicalDerivationsMatchV1Vector() {
        val androidPeerId = PeerId.fromBytes(range(0x00, 16))
        val windowsPeerId = PeerId.fromBytes(range(0x10, 16))
        val androidNonce = range(0x20, 32)
        val windowsNonce = range(0x40, 32)
        val androidPublicKey = publicKey(0x60)
        val windowsPublicKey = publicKey(0xA0)
        val sharedSecret = range(0xC0, 32)
        val sessionId = SessionId.fromBytes(range(0xE0, 16))

        val transcriptHash =
            PairingCrypto.computePairingTranscriptHash(
                androidPeerId = androidPeerId,
                windowsPeerId = windowsPeerId,
                androidNonce = androidNonce,
                windowsNonce = windowsNonce,
                androidPublicKey = androidPublicKey,
                windowsPublicKey = windowsPublicKey,
            )

        assertEquals(
            "ae3a44b49aa7a34878ed64efa63cfb5d3a06b041515fe3fb77a93f14377a6bb0",
            transcriptHash.hex(),
        )

        val pairingKey =
            PairingCrypto.derivePairingKey(
                ecdhSharedSecret = sharedSecret,
                pairingTranscriptHash = transcriptHash,
            )

        assertEquals(
            "8a12e9de1c2df8d5e83d7ab02effffc2d543d52af0da156fd912138dc8c791cb",
            pairingKey.hex(),
        )

        assertEquals(
            "432656",
            PairingCrypto.deriveSixDigitSas(
                pairingKey = pairingKey,
                pairingTranscriptHash = transcriptHash,
            ),
        )

        val pairingResponseProof =
            PairingCrypto.computePairingResponseProof(
                pairingKey = pairingKey,
                pairingTranscriptHash = transcriptHash,
                sessionId = sessionId,
            )

        assertEquals(
            "69168c6fb8ab71e31e4cabd994c045261bbba267427408bfc3ad11a9e445dc6b",
            pairingResponseProof.hex(),
        )

        val trustSecret =
            PairingCrypto.deriveTrustSecret(
                pairingKey = pairingKey,
                pairingTranscriptHash = transcriptHash,
            )

        assertEquals(
            "014850a457b18a4a55758249ade00423852dfb44a6c4a9a8bc422a28379ede4f",
            trustSecret.hex(),
        )

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

    private fun ByteArray.hex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
