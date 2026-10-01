package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
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

class UsbAccessoryRealtimeConnectorTest {
    @Test
    fun accessoryJoinsCurrentSessionAndPublishesState() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()
        val sessionId = SessionId.createRandom()
        val sessionKey =
            ByteArray(32) { index ->
                (index + 30).toByte()
            }

        TrustedSessionRegistry().use { registry ->
            registry.replace(
                peerId = windowsPeer,
                sessionId = sessionId,
                sessionKey = sessionKey,
            )

            val remoteFrames =
                buildRemoteJoinFrames(
                    windowsPeer,
                    sessionId,
                    sessionKey,
                )

            val connection =
                FakeConnection(
                    ByteArrayInputStream(
                        remoteFrames,
                    ),
                )

            val connector =
                UsbAccessoryRealtimeConnector(
                    connectionProvider =
                        UsbAccessoryConnectionProvider {
                            connection
                        },
                    joinClient =
                        UsbSecondarySessionJoinClient(
                            localPeerId = androidPeer,
                            receiverPeerId = windowsPeer,
                            sessionRegistry = registry,
                            monotonicMicros = { 500uL },
                        ),
                )

            val link = connector.connect()

            val state =
                GamepadState.Neutral.copy(
                    buttons = GamepadButtons.A,
                    leftX = 3210,
                )

            link.publish(
                RealtimeStateEnvelope(
                    sequence = 88u,
                    monotonicTimestampMicros = 900uL,
                    state = state,
                ),
            )

            waitForFrames(
                connection.output,
                minimum = 3,
            )

            val emitted =
                ByteArrayInputStream(
                    connection.output
                        .toByteArray(),
                )

            UsbTrustedSession(
                sessionId,
                sessionKey,
            ).use { verifier ->
                val ready =
                    UsbControlFrameCodec
                        .decodeSessionReady(
                            UsbStreamFrameCodec
                                .readFrame(emitted),
                            verifier,
                        )

                assertEquals(
                    PeerRole.ANDROID_CONTROLLER,
                    ready.role,
                )

                val transportReady =
                    UsbControlFrameCodec
                        .decodeTransportReady(
                            UsbStreamFrameCodec
                                .readFrame(emitted),
                            verifier,
                        )

                assertEquals(
                    ProtocolTransport.USB_DIRECT,
                    transportReady.transport,
                )

                val realtime =
                    ProtocolFrameCodec.decode(
                        UsbStreamFrameCodec
                            .readFrame(emitted),
                        verifier.authenticationKey(),
                    )

                assertEquals(
                    MessageType.GAMEPAD_STATE,
                    realtime.messageType,
                )
                assertTrue(
                    realtime.flags and
                        FrameFlags.AUTHENTICATED != 0,
                )
                assertEquals(
                    88u,
                    realtime.sequence,
                )
                assertEquals(
                    state,
                    GamepadStateCodec.decode(
                        realtime.payload,
                    ),
                )
            }

            link.close()
            assertTrue(connection.closed)
        }

        sessionKey.fill(0)
    }

    private fun buildRemoteJoinFrames(
        windowsPeer: PeerId,
        sessionId: SessionId,
        sessionKey: ByteArray,
    ): ByteArray {
        UsbTrustedSession(
            sessionId,
            sessionKey,
        ).use { session ->
            val ready =
                UsbControlFrameCodec
                    .encodeSessionReady(
                        session,
                        SessionReadyPayload(
                            role =
                                PeerRole.WINDOWS_RECEIVER,
                            capabilities =
                                TransportCapabilities.WIFI or
                                    TransportCapabilities.BLUETOOTH or
                                    TransportCapabilities.USB_DIRECT,
                            peerId = windowsPeer,
                        ),
                        100uL,
                    )

            val transportReady =
                UsbControlFrameCodec
                    .encodeTransportReady(
                        session,
                        200uL,
                    )

            return UsbStreamFrameCodec.encode(
                ready,
            ) +
                UsbStreamFrameCodec.encode(
                    transportReady,
                )
        }
    }

    private fun waitForFrames(
        output: ByteArrayOutputStream,
        minimum: Int,
    ) {
        val deadline =
            System.nanoTime() +
                2_000_000_000L

        while (
            countFrames(
                output.toByteArray(),
            ) < minimum &&
            System.nanoTime() < deadline
        ) {
            Thread.sleep(5)
        }

        assertTrue(
            countFrames(
                output.toByteArray(),
            ) >= minimum,
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
                UsbStreamFrameCodec
                    .readFrame(input)
            }.getOrNull()
                ?: break

            count += 1
        }

        return count
    }

    private class FakeConnection(
        override val inputStream: InputStream,
        val output: ByteArrayOutputStream =
            ByteArrayOutputStream(),
    ) : UsbAccessoryConnection {
        var closed = false
            private set

        override val outputStream: OutputStream
            get() = output

        override fun close() {
            closed = true
            runCatching {
                inputStream.close()
            }
            runCatching {
                output.close()
            }
        }
    }
}
