package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TrustedSessionRegistryTest {
    @Test
    fun replaceAndGetUseDefensiveKeyCopies() {
        TrustedSessionRegistry().use { registry ->
            val peerId = PeerId.createRandom()
            val sessionId = SessionId.createRandom()
            val original =
                ByteArray(32) { index ->
                    index.toByte()
                }
            val expected =
                original.copyOf()

            registry.replace(
                peerId = peerId,
                sessionId = sessionId,
                sessionKey = original,
            )

            original.fill(0)

            registry.get(peerId)!!.use { stored ->
                assertEquals(
                    sessionId,
                    stored.sessionId,
                )

                val firstCopy =
                    stored.copySessionKey()

                assertArrayEquals(
                    expected,
                    firstCopy,
                )

                firstCopy[0] =
                    (firstCopy[0].toInt() xor 0xFF)
                        .toByte()
            }

            registry.get(peerId)!!.use { reread ->
                assertArrayEquals(
                    expected,
                    reread.copySessionKey(),
                )
            }

            expected.fill(0)
        }
    }

    @Test
    fun getBySessionIdReturnsPeerAndCopy() {
        TrustedSessionRegistry().use { registry ->
            val firstPeer = PeerId.createRandom()
            val secondPeer = PeerId.createRandom()
            val firstSession =
                SessionId.createRandom()
            val secondSession =
                SessionId.createRandom()
            val firstKey =
                ByteArray(32) { 3 }
            val secondKey =
                ByteArray(32) { 4 }

            registry.replace(
                firstPeer,
                firstSession,
                firstKey,
            )
            registry.replace(
                secondPeer,
                secondSession,
                secondKey,
            )

            registry
                .getBySessionId(
                    secondSession,
                )!!
                .use { registration ->
                    assertEquals(
                        secondPeer,
                        registration.peerId,
                    )
                    assertEquals(
                        secondSession,
                        registration.material.sessionId,
                    )
                }

            assertNull(
                registry.getBySessionId(
                    SessionId.createRandom(),
                ),
            )

            firstKey.fill(0)
            secondKey.fill(0)
        }
    }

    @Test
    fun removeIfSessionDoesNotDeleteNewerReplacement() {
        TrustedSessionRegistry().use { registry ->
            val peerId = PeerId.createRandom()
            val oldSession = SessionId.createRandom()
            val newSession = SessionId.createRandom()
            val oldKey = ByteArray(32) { 5 }
            val newKey = ByteArray(32) { 6 }

            registry.replace(
                peerId,
                oldSession,
                oldKey,
            )

            registry.replace(
                peerId,
                newSession,
                newKey,
            )

            assertEquals(
                false,
                registry.removeIfSession(
                    peerId,
                    oldSession,
                ),
            )

            registry.get(peerId)!!.use { current ->
                assertEquals(
                    newSession,
                    current.sessionId,
                )
            }

            assertEquals(
                true,
                registry.removeIfSession(
                    peerId,
                    newSession,
                ),
            )

            assertNull(
                registry.get(peerId),
            )

            oldKey.fill(0)
            newKey.fill(0)
        }
    }

    @Test
    fun replacementAndRemovalTrackCurrentSession() {
        TrustedSessionRegistry().use { registry ->
            val peerId = PeerId.createRandom()
            val firstSession =
                SessionId.createRandom()
            val secondSession =
                SessionId.createRandom()
            val firstKey =
                ByteArray(32) { 1 }
            val secondKey =
                ByteArray(32) { 2 }

            registry.replace(
                peerId,
                firstSession,
                firstKey,
            )

            val first =
                registry.get(peerId)!!

            registry.replace(
                peerId,
                secondSession,
                secondKey,
            )

            val current =
                registry.get(peerId)!!

            first.use {
                assertEquals(
                    firstSession,
                    it.sessionId,
                )
            }

            current.use {
                assertEquals(
                    secondSession,
                    it.sessionId,
                )
            }

            registry.remove(peerId)
            assertNull(
                registry.get(peerId),
            )

            firstKey.fill(0)
            secondKey.fill(0)
        }
    }
}
