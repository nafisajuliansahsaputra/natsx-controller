package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PairingInvitationTest {
    @Test
    fun canonicalInvitationRoundTripsAndRedactsSecret() {
        val peer = PeerId.fromBytes(ByteArray(16) { it.toByte() })
        val secret = ByteArray(32) { index -> (0x20 + index).toByte() }

        val invitation = PairingInvitation(
            peer,
            secret,
            1_790_800_000L,
        )

        invitation.use {
            val encoded = it.toUriString()

            assertEquals(
                "natsx://pair/v1?peer=000102030405060708090a0b0c0d0e0f" +
                    "&secret=ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8" +
                    "&expires=1790800000",
                encoded,
            )

            PairingInvitation.parse(encoded).use { parsed ->
                assertEquals(peer, parsed.receiverPeerId)
                assertArrayEquals(secret, parsed.copySecret())
                assertEquals(1_790_800_000L, parsed.expiresUnixSeconds)
                assertTrue(parsed.toString().contains("[redacted]"))
                assertFalse(
                    parsed.toString().contains(
                        "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8",
                    ),
                )
            }
        }
    }

    @Test(expected = IllegalArgumentException::class)
    fun wrongSchemeIsRejected() {
        PairingInvitation.parse(
            "https://pair/v1?peer=000102030405060708090a0b0c0d0e0f" +
                "&secret=ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8" +
                "&expires=1790800000",
        )
    }
}
