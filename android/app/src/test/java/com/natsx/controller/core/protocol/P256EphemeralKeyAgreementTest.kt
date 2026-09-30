package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class P256EphemeralKeyAgreementTest {
    @Test
    fun bothPeersDeriveSameRawSharedSecret() {
        P256EphemeralKeyAgreement().use { android ->
            P256EphemeralKeyAgreement().use { windows ->
                val androidPublic = android.exportPublicKey()
                val windowsPublic = windows.exportPublicKey()
                val androidSecret =
                    android.deriveSharedSecret(windowsPublic)
                val windowsSecret =
                    windows.deriveSharedSecret(androidPublic)

                try {
                    assertEquals(
                        PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
                        androidPublic.size,
                    )
                    assertEquals(0x04, androidPublic[0].toInt())
                    assertEquals(
                        PairingCrypto.DERIVED_KEY_SIZE,
                        androidSecret.size,
                    )
                    assertArrayEquals(androidSecret, windowsSecret)
                } finally {
                    androidSecret.fill(0)
                    windowsSecret.fill(0)
                }
            }
        }
    }
}
