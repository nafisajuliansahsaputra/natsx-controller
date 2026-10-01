package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class PairingPayloadCodecTest {
    @Test
    fun canonicalPayloadsAndConfirmationProofsMatchFrozenVectors() {
        val androidPeer =
            PeerId.fromBytes("000102030405060708090a0b0c0d0e0f".hex())
        val windowsPeer =
            PeerId.fromBytes("101112131415161718191a1b1c1d1e1f".hex())
        val androidNonce =
            "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f".hex()
        val windowsNonce =
            "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f".hex()
        val androidKey =
            ("04606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f" +
                "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f").hex()
        val windowsKey =
            ("04a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebf" +
                "c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf").hex()
        val responseProof =
            "69168c6fb8ab71e31e4cabd994c045261bbba267427408bfc3ad11a9e445dc6b".hex()

        val hello =
            PairingPayloadCodec.encodeHello(
                PairingHelloPayload(
                    androidPeer,
                    TransportCapabilities.WIFI or
                        TransportCapabilities.BLUETOOTH or
                        TransportCapabilities.USB_DIRECT,
                    androidNonce,
                    androidKey,
                ),
            )

        val response =
            PairingPayloadCodec.encodeResponse(
                PairingResponsePayload(
                    windowsPeer,
                    TransportCapabilities.WIFI or
                        TransportCapabilities.BLUETOOTH or
                        TransportCapabilities.USB_DIRECT,
                    windowsNonce,
                    windowsKey,
                    responseProof,
                ),
            )

        assertEquals(
            "000102030405060708090a0b0c0d0e0f07000000" +
                "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f" +
                "04606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f" +
                "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f",
            hello.hexString(),
        )

        assertEquals(
            "101112131415161718191a1b1c1d1e1f07000000" +
                "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f" +
                "04a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebf" +
                "c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf" +
                "69168c6fb8ab71e31e4cabd994c045261bbba267427408bfc3ad11a9e445dc6b",
            response.hexString(),
        )

        val pairingKey =
            "8a12e9de1c2df8d5e83d7ab02effffc2d543d52af0da156fd912138dc8c791cb".hex()
        val transcriptHash =
            "ae3a44b49aa7a34878ed64efa63cfb5d3a06b041515fe3fb77a93f14377a6bb0".hex()
        val sessionId =
            SessionId.fromBytes("e0e1e2e3e4e5e6e7e8e9eaebecedeeef".hex())

        val androidProof =
            PairingCrypto.computePairingConfirmationProof(
                PairingConfirmationRole.ANDROID,
                pairingKey,
                transcriptHash,
                sessionId,
            )
        val windowsProof =
            PairingCrypto.computePairingConfirmationProof(
                PairingConfirmationRole.WINDOWS,
                pairingKey,
                transcriptHash,
                sessionId,
            )

        assertEquals(
            "5901a5130c82c7fc1655c5504bf00aeb5c1e0678058f6b5b43d20161b1b02b7f",
            androidProof.hexString(),
        )
        assertEquals(
            "5b85b56da7b3d414441a0d22138e9334b99ab5cb448de7c316ac9b0a2fc82fe1",
            windowsProof.hexString(),
        )
        assertFalse(
            PairingCrypto.verifyPairingConfirmationProof(
                PairingConfirmationRole.WINDOWS,
                pairingKey,
                transcriptHash,
                sessionId,
                androidProof,
            ),
        )
    }

    private fun String.hex(): ByteArray =
        chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun ByteArray.hexString(): String =
        joinToString("") { "%02x".format(it.toInt() and 0xff) }
}
