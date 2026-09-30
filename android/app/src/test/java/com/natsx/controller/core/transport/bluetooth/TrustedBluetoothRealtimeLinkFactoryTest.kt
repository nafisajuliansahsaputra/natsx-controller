package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.trust.TrustedPeerMaterial
import com.natsx.controller.core.trust.TrustedPeerRecord
import com.natsx.controller.core.trust.TrustedPeerStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

class TrustedBluetoothRealtimeLinkFactoryTest {
    @Test
    fun skipsFailedCandidateAndUsesAuthenticatedSessionJoin() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val sessionId = SessionId.createRandom()
        val sessionKey =
            ByteArray(32) { index ->
                (index + 20).toByte()
            }

        TrustedSessionRegistry().use { registry ->
            registry.replace(
                peerId = windowsPeer,
                sessionId = sessionId,
                sessionKey = sessionKey,
            )

            val windowsReady: ByteArray
            val windowsTransportReady: ByteArray

            BluetoothTrustedSession(
                sessionId,
                sessionKey,
            ).use { windowsSession ->
                windowsReady =
                    BluetoothControlFrameCodec
                        .encodeSessionReady(
                            windowsSession,
                            SessionReadyPayload(
                                role =
                                    PeerRole.WINDOWS_RECEIVER,
                                capabilities =
                                    TransportCapabilities.BLUETOOTH,
                                peerId = windowsPeer,
                            ),
                            100uL,
                        )

                windowsTransportReady =
                    BluetoothControlFrameCodec
                        .encodeTransportReady(
                            windowsSession,
                            ProtocolTransport.BLUETOOTH,
                            200uL,
                        )
            }

            val successfulConnection =
                FakeConnection(
                    input =
                        ByteArrayInputStream(
                            BluetoothStreamFrameCodec
                                .encode(windowsReady) +
                                BluetoothStreamFrameCodec
                                    .encode(
                                        windowsTransportReady,
                                    ),
                        ),
                )

            val firstCalls =
                AtomicInteger(0)
            val secondCalls =
                AtomicInteger(0)

            val factory =
                TrustedBluetoothRealtimeLinkFactory(
                    localPeerId = androidPeer,
                    receiverPeerId = windowsPeer,
                    trustedPeerStore =
                        EmptyTrustedPeerStore(),
                    sessionRegistry = registry,
                    candidateProvider =
                        BluetoothRfcommCandidateProvider {
                            listOf(
                                BluetoothRfcommCandidate(
                                    routeId = "bad",
                                    connect = {
                                        firstCalls
                                            .incrementAndGet()
                                        throw IOException(
                                            "simulated route failure",
                                        )
                                    },
                                ),
                                BluetoothRfcommCandidate(
                                    routeId = "good",
                                    connect = {
                                        secondCalls
                                            .incrementAndGet()
                                        successfulConnection
                                    },
                                ),
                            )
                        },
                )

            val link = factory.create()

            try {
                assertEquals(
                    1,
                    firstCalls.get(),
                )
                assertEquals(
                    1,
                    secondCalls.get(),
                )
                assertTrue(
                    successfulConnection
                        .output
                        .size() > 0,
                )
            } finally {
                link.close()
            }

            assertTrue(
                successfulConnection.closed.get(),
            )
        }

        sessionKey.fill(0)
    }

    private class FakeConnection(
        input: InputStream,
    ) : BluetoothRfcommConnection {
        override val inputStream:
            InputStream =
            input

        val output =
            ByteArrayOutputStream()

        override val outputStream:
            OutputStream =
            output

        val closed =
            AtomicBoolean(false)

        override fun close() {
            closed.set(true)
            runCatching {
                inputStream.close()
            }
        }
    }

    private class EmptyTrustedPeerStore :
        TrustedPeerStore {
        override fun put(
            record: TrustedPeerRecord,
            trustSecret: ByteArray,
        ) {
            error(
                "not used",
            )
        }

        override fun get(
            peerId: PeerId,
        ): TrustedPeerMaterial? =
            null

        override fun list():
            List<TrustedPeerRecord> =
            emptyList()

        override fun remove(
            peerId: PeerId,
        ) {
        }

        override fun clear() {
        }
    }
}
