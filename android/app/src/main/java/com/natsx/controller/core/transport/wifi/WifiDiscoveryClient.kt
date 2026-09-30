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
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.security.SecureRandom

data class WifiDiscoveredReceiver(
    val peerId: PeerId,
    val endpoint: InetSocketAddress,
    val capabilities: Int,
)

/**
 * Local LAN discovery. This class only discovers an endpoint; it never marks a
 * receiver trusted and never creates controller authorization by itself.
 */
class WifiDiscoveryClient(
    private val localPeerId: PeerId,
    private val timeoutMillis: Int = DEFAULT_DISCOVERY_TIMEOUT_MILLIS,
) : WifiReceiverDiscovery {
    init {
        require(timeoutMillis in 100..10_000)
    }

    fun discover(): WifiDiscoveredReceiver? = discover(null)

    override fun discover(
        expectedReceiverPeerId: PeerId?,
    ): WifiDiscoveredReceiver? {
        val nonce = SecureRandom().nextInt().toUInt()
        val requestPayload = HelloPayload(
            role = PeerRole.ANDROID_CONTROLLER,
            capabilities =
                TransportCapabilities.WIFI or
                    TransportCapabilities.BLUETOOTH or
                    TransportCapabilities.USB_DIRECT,
            peerId = localPeerId,
            realtimePort = 0,
            discoveryNonce = nonce,
        )

        val request = ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HELLO,
                flags = FrameFlags.NONE,
                sessionId = SessionId.Zero,
                sequence = 0u,
                monotonicTimestampMicros = monotonicMicroseconds(),
                payload = HelloPayloadCodec.encode(requestPayload),
            ),
        )

        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = timeoutMillis

            val discoveryEndpoint = InetSocketAddress(
                InetAddress.getByName(LIMITED_BROADCAST_ADDRESS),
                DISCOVERY_PORT,
            )

            socket.send(
                DatagramPacket(
                    request,
                    request.size,
                    discoveryEndpoint,
                ),
            )

            val deadlineNanos =
                SystemClock.elapsedRealtimeNanos() +
                    timeoutMillis.toLong() * 1_000_000L

            val buffer = ByteArray(MAXIMUM_DISCOVERY_DATAGRAM_SIZE)

            while (SystemClock.elapsedRealtimeNanos() < deadlineNanos) {
                val remainingMillis =
                    (
                        (deadlineNanos - SystemClock.elapsedRealtimeNanos()) /
                            1_000_000L
                    ).coerceAtLeast(1L)

                socket.soTimeout =
                    remainingMillis.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()

                val packet = DatagramPacket(buffer, buffer.size)

                try {
                    socket.receive(packet)
                } catch (_: SocketTimeoutException) {
                    return null
                }

                val response = packet.data.copyOfRange(
                    packet.offset,
                    packet.offset + packet.length,
                )

                val discovered = decodeResponse(
                    response = response,
                    sourceAddress = packet.address,
                    expectedNonce = nonce,
                ) ?: continue

                if (
                    expectedReceiverPeerId == null ||
                    discovered.peerId == expectedReceiverPeerId
                ) {
                    return discovered
                }
            }
        }

        return null
    }

    private fun decodeResponse(
        response: ByteArray,
        sourceAddress: InetAddress,
        expectedNonce: UInt,
    ): WifiDiscoveredReceiver? {
        return try {
            val frame = ProtocolFrameCodec.decode(response)

            if (
                frame.messageType != MessageType.HELLO ||
                frame.flags != FrameFlags.NONE ||
                frame.sessionId != SessionId.Zero
            ) {
                return null
            }

            val hello = HelloPayloadCodec.decode(frame.payload)

            if (
                hello.role != PeerRole.WINDOWS_RECEIVER ||
                hello.discoveryNonce != expectedNonce ||
                hello.capabilities and TransportCapabilities.WIFI == 0 ||
                hello.realtimePort == 0
            ) {
                return null
            }

            WifiDiscoveredReceiver(
                peerId = hello.peerId,
                endpoint = InetSocketAddress(
                    sourceAddress,
                    hello.realtimePort,
                ),
                capabilities = hello.capabilities,
            )
        } catch (_: IllegalArgumentException) {
            null
        }
    }

    private fun monotonicMicroseconds(): ULong =
        SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL

    companion object {
        const val DISCOVERY_PORT = 43859
        const val DEFAULT_DISCOVERY_TIMEOUT_MILLIS = 1_000
        const val MAXIMUM_DISCOVERY_DATAGRAM_SIZE = 512
        const val LIMITED_BROADCAST_ADDRESS = "255.255.255.255"
    }
}
