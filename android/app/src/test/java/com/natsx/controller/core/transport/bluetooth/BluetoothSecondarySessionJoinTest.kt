package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class BluetoothSecondarySessionJoinTest {
    @Test
    fun joinReusesRegistrySessionAndEmitsTransportReady() {
        val androidPeer =
            PeerId.createRandom()
        val windowsPeer =
            PeerId.createRandom()
        val sessionId =
            SessionId.createRandom()
        val sessionKey =
            ByteArray(32) { index ->
                (index + 10).toByte()
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
                                    TransportCapabilities.WIFI or
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

            val input =
                ByteArrayInputStream(
                    concat(
                        BluetoothStreamFrameCodec
                            .encode(windowsReady),
                        BluetoothStreamFrameCodec
                            .encode(
                                windowsTransportReady,
                            ),
                    ),
                )

            val output =
                ByteArrayOutputStream()

            val client =
                BluetoothSecondarySessionJoinClient(
                    localPeerId = androidPeer,
                    receiverPeerId = windowsPeer,
                    sessionRegistry = registry,
                    monotonicMicros = {
                        300uL
                    },
                )

            client.join(
                input,
                output,
            ).use { joined ->
                assertEquals(
                    sessionId,
                    joined.sessionId,
                )

                val emittedInput =
                    ByteArrayInputStream(
                        output.toByteArray(),
                    )

                val localReadyFrame =
                    BluetoothStreamFrameCodec
                        .readFrame(emittedInput)

                val localTransportReadyFrame =
                    BluetoothStreamFrameCodec
                        .readFrame(emittedInput)

                BluetoothTrustedSession(
                    sessionId,
                    sessionKey,
                ).use { verifier ->
                    val localReady =
                        BluetoothControlFrameCodec
                            .decodeSessionReady(
                                localReadyFrame,
                                verifier,
                            )

                    assertEquals(
                        PeerRole.ANDROID_CONTROLLER,
                        localReady.role,
                    )
                    assertEquals(
                        androidPeer,
                        localReady.peerId,
                    )
                    assertTrue(
                        localReady.capabilities and
                            TransportCapabilities.BLUETOOTH != 0,
                    )

                    val localTransportReady =
                        BluetoothControlFrameCodec
                            .decodeTransportReady(
                                localTransportReadyFrame,
                                verifier,
                            )

                    assertEquals(
                        ProtocolTransport.BLUETOOTH,
                        localTransportReady.transport,
                    )
                }
            }
        }

        sessionKey.fill(0)
    }

    private fun concat(
        first: ByteArray,
        second: ByteArray,
    ): ByteArray =
        first + second
}
