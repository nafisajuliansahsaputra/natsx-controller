package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.HelloPayload
import com.natsx.controller.core.protocol.HelloPayloadCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TransportCapabilities
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.security.SecureRandom

/**
 * Fast cached-endpoint probe used before a trusted reconnect session exists.
 *
 * The HELLO response is only endpoint discovery. Matching a Peer ID does not
 * authorize controller input; authorization still requires AUTH_CHALLENGE /
 * AUTH_RESPONSE before a realtime link is created.
 */
class TrustedReceiverHelloProbe(
    private val localPeerId: PeerId,
    private val expectedReceiverPeerId: PeerId,
    private val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
) : WifiEndpointProbe {
    init {
        require(timeoutMillis in 50..5_000)
    }

    override fun isReachable(endpoint: InetSocketAddress): Boolean {
        val controlEndpoint =
            WifiTrustedReconnectClient.controlEndpointForRealtime(endpoint)
        val nonce = SecureRandom().nextInt().toUInt()

        val request = ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HELLO,
                flags = FrameFlags.NONE,
                sessionId = SessionId.Zero,
                sequence = 0u,
                monotonicTimestampMicros =
                    SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL,
                payload = HelloPayloadCodec.encode(
                    HelloPayload(
                        role = PeerRole.ANDROID_CONTROLLER,
                        capabilities =
                            TransportCapabilities.WIFI or
                                TransportCapabilities.BLUETOOTH or
                                TransportCapabilities.USB_DIRECT,
                        peerId = localPeerId,
                        realtimePort = 0,
                        discoveryNonce = nonce,
                    ),
                ),
            ),
        )

        return try {
            DatagramSocket().use { socket ->
                socket.connect(controlEndpoint)
                socket.soTimeout = timeoutMillis
                socket.send(
                    DatagramPacket(
                        request,
                        request.size,
                        controlEndpoint,
                    ),
                )

                val buffer = ByteArray(MAXIMUM_DATAGRAM_SIZE)
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)

                val frame = ProtocolFrameCodec.decode(
                    packet.data.copyOfRange(
                        packet.offset,
                        packet.offset + packet.length,
                    ),
                )

                if (
                    frame.messageType != MessageType.HELLO ||
                    frame.flags != FrameFlags.NONE ||
                    frame.sessionId != SessionId.Zero
                ) {
                    return false
                }

                val hello = HelloPayloadCodec.decode(frame.payload)

                hello.role == PeerRole.WINDOWS_RECEIVER &&
                    hello.peerId == expectedReceiverPeerId &&
                    hello.discoveryNonce == nonce &&
                    hello.capabilities and TransportCapabilities.WIFI != 0 &&
                    hello.realtimePort == endpoint.port
            }
        } catch (_: SocketTimeoutException) {
            false
        } catch (_: IOException) {
            false
        } catch (_: IllegalArgumentException) {
            false
        }
    }

    companion object {
        const val DEFAULT_TIMEOUT_MILLIS = 350
        const val MAXIMUM_DATAGRAM_SIZE = 512
    }
}
