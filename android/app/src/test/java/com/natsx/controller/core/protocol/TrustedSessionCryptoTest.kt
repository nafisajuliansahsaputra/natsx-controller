package com.natsx.controller.core.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TrustedSessionCryptoTest {
    @Test
    fun sessionKeyAndProofMatchCsharpVector() {
        val rootKey = ByteArray(32) { it.toByte() }
        val challenge = ByteArray(32) { (it + 32).toByte() }

        val androidDeviceId = SessionId.fromBytes(
            hex("00112233445566778899aabbccddeeff"),
        )
        val windowsDeviceId = SessionId.fromBytes(
            hex("ffeeddccbbaa99887766554433221100"),
        )
        val sessionId = SessionId.fromBytes(
            hex("102132435465768798a9bacbdcedfe0f"),
        )

        val sessionKey = TrustedSessionCrypto.deriveSessionKey(
            rootKey,
            challenge,
            androidDeviceId,
            windowsDeviceId,
            sessionId,
        )

        assertEquals(
            "6c6643338f73fab694ad625740b63c2887fb1fa2088d936c4223c68aa9edcb3f",
            sessionKey.toHex(),
        )

        val androidHello = hex(
            "00112233445566778899aabbccddeeff01070d00000001010001000000000000",
        )
        val windowsHello = hex(
            "ffeeddccbbaa9988776655443322110002070d00000001010001000000000000",
        )

        val proof = TrustedSessionCrypto.computeAndroidProof(
            sessionKey,
            androidHello,
            windowsHello,
            challenge,
            sessionId,
        )

        assertEquals(
            "f087a2a2731e681b4d1a767396547c25",
            proof.toHex(),
        )

        assertTrue(TrustedSessionCrypto.verifyAndroidProof(proof, proof))

        val wrong = proof.copyOf()
        wrong[0] = (wrong[0].toInt() xor 1).toByte()
        assertFalse(TrustedSessionCrypto.verifyAndroidProof(proof, wrong))
    }

    private fun hex(value: String): ByteArray =
        ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") {
            "%02x".format(it.toInt() and 0xFF)
        }
}
