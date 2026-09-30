package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.security.GeneralSecurityException

class WifiTrustedReconnectClient(
    private val localPeerId: PeerId,
    private val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL
    },
) {
    init {
        require(timeoutMillis in 100..5_000)
    }

    fun connect(
        controlEndpoint: InetSocketAddress,
        receiverPeerId: PeerId,
        trustSecret: ByteArray,
    ): WifiTrustedSession {
        require(controlEndpoint.port in 1..65535)
        require(
            trustSecret.size ==
                com.natsx.controller.core.protocol.TrustedReconnectCrypto.TRUST_KEY_SIZE,
        ) {
            "Trust secret must be exactly 32 bytes."
        }

        WifiTrustedHandshakeChallenge.create(
            localPeerId = localPeerId,
            receiverPeerId = receiverPeerId,
            trustSecret = trustSecret,
        ).use { challenge ->
            DatagramSocket().use { socket ->
                socket.connect(controlEndpoint)
                socket.soTimeout = timeoutMillis

                send(
                    socket,
                    controlEndpoint,
                    challenge.encodeChallenge(monotonicMicros()),
                )

                val responseBytes = receive(socket)
                val trustedSession =
                    challenge.acceptResponse(responseBytes)

                try {
                    val localReady = SessionReadyPayload(
                        role = PeerRole.ANDROID_CONTROLLER,
                        capabilities =
                            TransportCapabilities.WIFI or
                                TransportCapabilities.BLUETOOTH or
                                TransportCapabilities.USB_DIRECT,
                        peerId = localPeerId,
                    )

                    send(
                        socket,
                        controlEndpoint,
                        WifiControlDatagramCodec.encodeSessionReady(
                            trustedSession = trustedSession,
                            payload = localReady,
                            monotonicTimestampMicros = monotonicMicros(),
                        ),
                    )

                    val remoteReady =
                        WifiControlDatagramCodec.decodeSessionReady(
                            datagram = receive(socket),
                            trustedSession = trustedSession,
                        )

                    if (
                        remoteReady.role != PeerRole.WINDOWS_RECEIVER ||
                        remoteReady.peerId != receiverPeerId ||
                        remoteReady.capabilities and
                            TransportCapabilities.WIFI == 0
                    ) {
                        throw GeneralSecurityException(
                            "SESSION_READY does not match the trusted Windows receiver.",
                        )
                    }

                    return trustedSession
                } catch (exception: Exception) {
                    trustedSession.close()
                    throw exception
                }
            }
        }
    }

    private fun send(
        socket: DatagramSocket,
        endpoint: InetSocketAddress,
        bytes: ByteArray,
    ) {
        socket.send(
            DatagramPacket(
                bytes,
                bytes.size,
                endpoint,
            ),
        )
    }

    private fun receive(socket: DatagramSocket): ByteArray {
        val buffer = ByteArray(MAXIMUM_DATAGRAM_SIZE)
        val packet = DatagramPacket(buffer, buffer.size)

        try {
            socket.receive(packet)
        } catch (exception: SocketTimeoutException) {
            throw IOException(
                "Timed out waiting for trusted Wi-Fi handshake response.",
                exception,
            )
        }

        return packet.data.copyOfRange(
            packet.offset,
            packet.offset + packet.length,
        )
    }

    companion object {
        const val DEFAULT_TIMEOUT_MILLIS = 1_000
        const val MAXIMUM_DATAGRAM_SIZE = 512

        fun controlEndpointForRealtime(
            realtimeEndpoint: InetSocketAddress,
        ): InetSocketAddress =
            InetSocketAddress(
                realtimeEndpoint.address,
                WifiDiscoveryClient.DISCOVERY_PORT,
            )
    }
}
