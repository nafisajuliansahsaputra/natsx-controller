package com.natsx.controller.core.protocol

import java.security.KeyFactory
import java.security.KeyPair
import java.security.spec.PKCS8EncodedKeySpec
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class PairingProtocolTest {
    private val androidPrivateKeyHex =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104200000000000000000000000000000000000000000000000000000000000000001a144034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5"

    private val windowsPrivateKeyHex =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104200000000000000000000000000000000000000000000000000000000000000002a144034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d1"

    private val androidId = SessionId.fromBytes(
        hex("00112233445566778899aabbccddeeff"),
    )

    private val windowsId = SessionId.fromBytes(
        hex("ffeeddccbbaa99887766554433221100"),
    )

    @Test
    fun pairingCodecMatchesCsharpVectors() {
        val android = importPrivate(androidPrivateKeyHex)
        val windows = importPrivate(windowsPrivateKeyHex)

        val request = PairingCodec.encodeRequest(
            PairingHelloPayload(
                deviceId = androidId,
                publicKeyDer = PairingCrypto.exportPublicKey(android),
                displayName = "Android",
            ),
        )

        val response = PairingCodec.encodeResponse(
            PairingHelloPayload(
                deviceId = windowsId,
                publicKeyDer = PairingCrypto.exportPublicKey(windows),
                displayName = "LEGION",
            ),
        )

        assertEquals(
            "4e5850310100010000112233445566778899aabbccddeeff5b0007003059301306072a8648ce3d020106082a8648ce3d030107034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5416e64726f6964",
            request.toHex(),
        )

        assertEquals(
            "4e58503101000200ffeeddccbbaa998877665544332211005b0006003059301306072a8648ce3d020106082a8648ce3d030107034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d14c4547494f4e",
            response.toHex(),
        )

        assertEquals(
            androidId,
            PairingCodec.decodeRequest(request).deviceId,
        )
        assertEquals(
            windowsId,
            PairingCodec.decodeResponse(response).deviceId,
        )
    }

    @Test
    fun pairingCryptoMatchesCsharpVectors() {
        val android = importPrivate(androidPrivateKeyHex)
        val windows = importPrivate(windowsPrivateKeyHex)

        val request = PairingCodec.encodeRequest(
            PairingHelloPayload(
                androidId,
                PairingCrypto.exportPublicKey(android),
                "Android",
            ),
        )

        val response = PairingCodec.encodeResponse(
            PairingHelloPayload(
                windowsId,
                PairingCrypto.exportPublicKey(windows),
                "LEGION",
            ),
        )

        val transcript =
            PairingCrypto.computeTranscriptHash(
                request,
                response,
            )

        assertEquals(
            "e8a010dbb7b3bf43804ab65332deb4ad064b1d9f70454678dfc348993ff5d4ef",
            transcript.toHex(),
        )

        val androidRoot =
            PairingCrypto.derivePairingRootKey(
                android,
                PairingCrypto.exportPublicKey(windows),
                transcript,
            )

        val windowsRoot =
            PairingCrypto.derivePairingRootKey(
                windows,
                PairingCrypto.exportPublicKey(android),
                transcript,
            )

        assertArrayEquals(androidRoot, windowsRoot)

        assertEquals(
            "d0b7e0a0fb04b38d46e439369ebdabaee772e5b623a121e4b16a2cf19928c00a",
            androidRoot.toHex(),
        )

        assertEquals(
            "609252",
            PairingCrypto.computeSas(
                androidRoot,
                transcript,
            ),
        )

        assertEquals(
            "516a3d71a88c2b821ca714984076bcf0",
            PairingCrypto.computeConfirmationTag(
                PairingRole.ANDROID,
                androidRoot,
                transcript,
            ).toHex(),
        )

        assertEquals(
            "5b1126444b5610ed17dbc7ae0fa87906",
            PairingCrypto.computeConfirmationTag(
                PairingRole.WINDOWS,
                androidRoot,
                transcript,
            ).toHex(),
        )

        assertEquals(
            "b54dd1f9d7b392a8a86492dd6424f019",
            PairingCrypto.computeCompletionTag(
                androidRoot,
                transcript,
            ).toHex(),
        )

        androidRoot.fill(0)
        windowsRoot.fill(0)
        transcript.fill(0)
    }

    private fun importPrivate(hexValue: String): KeyPair {
        val factory = KeyFactory.getInstance("EC")
        val privateKey = factory.generatePrivate(
            PKCS8EncodedKeySpec(hex(hexValue)),
        )

        val publicKeyDer =
            if (hexValue == androidPrivateKeyHex) {
                hex(
                    "3059301306072a8648ce3d020106082a8648ce3d030107034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5",
                )
            } else {
                hex(
                    "3059301306072a8648ce3d020106082a8648ce3d030107034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d1",
                )
            }

        val publicKey = factory.generatePublic(
            java.security.spec.X509EncodedKeySpec(
                publicKeyDer,
            ),
        )

        return KeyPair(publicKey, privateKey)
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2)
                .toInt(16)
                .toByte()
        }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
