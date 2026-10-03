package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateEnvelope
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream

class BluetoothRfcommConnectorTest {
    @Test
    fun hungRfcommAttemptClosesSocketAndAllowsNextRetry() {
        val closed = java.util.concurrent.CountDownLatch(1)
        val socket = object : BluetoothRfcommSocket {
            override val inputStream: InputStream = ByteArrayInputStream(byteArrayOf())
            override val outputStream: OutputStream = ByteArrayOutputStream()
            override fun connect() {
                check(closed.await(2, java.util.concurrent.TimeUnit.SECONDS))
                throw java.io.IOException("Socket was closed by connection deadline")
            }
            override fun close() { closed.countDown() }
        }
        TrustedSessionRegistry().use { registry ->
            val connector = BluetoothRfcommConnector(
                BluetoothRfcommSocketProvider { socket },
                BluetoothSecondarySessionJoinClient(PeerId.createRandom(), PeerId.createRandom(), registry, { 1uL }),
                connectionTimeoutMillis = 100,
            )
            assertTrue(runCatching { connector.connect() }.exceptionOrNull() is java.io.IOException)
            assertEquals(0L, closed.count)
        }
    }

    @Test
    fun connectJoinsActiveSessionAndPublishesRealtimeState() {
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

            val remoteFrames =
                buildRemoteJoinFrames(
                    windowsPeer = windowsPeer,
                    sessionId = sessionId,
                    sessionKey = sessionKey,
                )

            val socket =
                FakeRfcommSocket(
                    input = ByteArrayInputStream(remoteFrames),
                )

            val connector =
                BluetoothRfcommConnector(
                    socketProvider =
                        BluetoothRfcommSocketProvider {
                            socket
                        },
                    secondaryJoinClient =
                        BluetoothSecondarySessionJoinClient(
                            localPeerId = androidPeer,
                            receiverPeerId = windowsPeer,
                            sessionRegistry = registry,
                            monotonicMicros = {
                                500uL
                            },
                        ),
                )

            val link = connector.connect()

            assertTrue(socket.connected)

            val state =
                GamepadState.Neutral.copy(
                    buttons = GamepadButtons.A,
                    leftX = 1234,
                )

            link.publish(
                RealtimeStateEnvelope(
                    sequence = 77u,
                    monotonicTimestampMicros = 900uL,
                    state = state,
                ),
            )

            waitForOutputFrames(
                socket.output,
                minimumFrames = 3,
            )

            val emitted =
                ByteArrayInputStream(
                    socket.output.toByteArray(),
                )

            BluetoothTrustedSession(
                sessionId,
                sessionKey,
            ).use { verifier ->
                val localReady =
                    BluetoothControlFrameCodec
                        .decodeSessionReady(
                            BluetoothStreamFrameCodec
                                .readFrame(emitted),
                            verifier,
                        )

                assertEquals(
                    PeerRole.ANDROID_CONTROLLER,
                    localReady.role,
                )

                val transportReady =
                    BluetoothControlFrameCodec
                        .decodeTransportReady(
                            BluetoothStreamFrameCodec
                                .readFrame(emitted),
                            verifier,
                        )

                assertEquals(
                    ProtocolTransport.BLUETOOTH,
                    transportReady.transport,
                )

                val realtimeFrame =
                    ProtocolFrameCodec.decode(
                        frameBytes =
                            BluetoothStreamFrameCodec
                                .readFrame(emitted),
                        authenticationKey =
                            verifier.authenticationKey(),
                    )

                assertEquals(
                    MessageType.GAMEPAD_STATE,
                    realtimeFrame.messageType,
                )
                assertTrue(
                    realtimeFrame.flags and
                        FrameFlags.AUTHENTICATED != 0,
                )
                assertEquals(
                    sessionId,
                    realtimeFrame.sessionId,
                )
                assertEquals(
                    77u,
                    realtimeFrame.sequence,
                )
                assertEquals(
                    state,
                    GamepadStateCodec.decode(
                        realtimeFrame.payload,
                    ),
                )
            }

            link.close()
            assertTrue(socket.closed)
        }

        sessionKey.fill(0)
    }

    @Test
    fun joinFailureClosesSocket() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()

        TrustedSessionRegistry().use { registry ->
            val socket =
                FakeRfcommSocket(
                    input =
                        ByteArrayInputStream(
                            byteArrayOf(),
                        ),
                )

            val connector =
                BluetoothRfcommConnector(
                    socketProvider =
                        BluetoothRfcommSocketProvider {
                            socket
                        },
                    secondaryJoinClient =
                        BluetoothSecondarySessionJoinClient(
                            localPeerId = androidPeer,
                            receiverPeerId = windowsPeer,
                            sessionRegistry = registry,
                        ),
                )

            runCatching {
                connector.connect()
            }

            assertTrue(socket.connected)
            assertTrue(socket.closed)
        }
    }

    private fun buildRemoteJoinFrames(
        windowsPeer: PeerId,
        sessionId: SessionId,
        sessionKey: ByteArray,
    ): ByteArray {
        BluetoothTrustedSession(
            sessionId,
            sessionKey,
        ).use { session ->
            val ready =
                BluetoothControlFrameCodec
                    .encodeSessionReady(
                        trustedSession = session,
                        payload =
                            SessionReadyPayload(
                                role =
                                    PeerRole.WINDOWS_RECEIVER,
                                capabilities =
                                    TransportCapabilities.WIFI or
                                        TransportCapabilities.BLUETOOTH,
                                peerId = windowsPeer,
                            ),
                        monotonicTimestampMicros = 100uL,
                    )

            val transportReady =
                BluetoothControlFrameCodec
                    .encodeTransportReady(
                        trustedSession = session,
                        transport =
                            ProtocolTransport.BLUETOOTH,
                        monotonicTimestampMicros = 200uL,
                    )

            return BluetoothStreamFrameCodec.encode(
                ready,
            ) +
                BluetoothStreamFrameCodec.encode(
                    transportReady,
                )
        }
    }

    private fun waitForOutputFrames(
        output: ByteArrayOutputStream,
        minimumFrames: Int,
    ) {
        val deadline =
            System.nanoTime() +
                2_000_000_000L

        while (
            countFrames(
                output.toByteArray(),
            ) < minimumFrames &&
            System.nanoTime() < deadline
        ) {
            Thread.sleep(5)
        }

        assertTrue(
            countFrames(
                output.toByteArray(),
            ) >= minimumFrames,
        )
    }

    private fun countFrames(
        bytes: ByteArray,
    ): Int {
        val input =
            ByteArrayInputStream(bytes)

        var count = 0

        while (input.available() > 0) {
            runCatching {
                BluetoothStreamFrameCodec
                    .readFrame(input)
            }.getOrNull()
                ?: break

            count += 1
        }

        return count
    }

    private class FakeRfcommSocket(
        private val input: InputStream,
        val output: ByteArrayOutputStream =
            ByteArrayOutputStream(),
    ) : BluetoothRfcommSocket {
        var connected: Boolean = false
            private set

        var closed: Boolean = false
            private set

        override val inputStream: InputStream
            get() = input

        override val outputStream: OutputStream
            get() = output

        override fun connect() {
            connected = true
        }

        override fun close() {
            closed = true
            runCatching {
                input.close()
            }
            runCatching {
                output.close()
            }
        }
    }
}
