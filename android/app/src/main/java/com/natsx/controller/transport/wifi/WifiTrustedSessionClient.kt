package com.natsx.controller.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.protocol.EstablishedTrustedSession
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.HelloPayload
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.TrustedReconnectClientHandshake
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException

data class EstablishedWifiSession(
    val remoteAddress: InetAddress,
    val remotePort: Int,
    val trustedSession: EstablishedTrustedSession,
)

class WifiTrustedSessionClient(
    private val androidHello: HelloPayload,
    private val expectedWindowsDeviceId: com.natsx.controller.core.protocol.SessionId,
    pairingRootKey: ByteArray,
) {
    private val pairingRootKey = pairingRootKey.copyOf()

    fun connect(
        remoteAddress: InetAddress,
        remotePort: Int = WifiControllerTransport.DEFAULT_PORT,
        timeoutMilliseconds: Int = DEFAULT_HANDSHAKE_TIMEOUT_MS,
    ): EstablishedWifiSession {
        require(remotePort in 1..65535)
        require(timeoutMilliseconds in 500..10_000)

        val handshake = TrustedReconnectClientHandshake(
            androidHello = androidHello,
            expectedWindowsDeviceId = expectedWindowsDeviceId,
            pairingRootKey = pairingRootKey,
            monotonicMicros = {
                (SystemClock.elapsedRealtimeNanos() / 1_000L).toULong()
            },
        )

        DatagramSocket(null).use { socket ->
            socket.reuseAddress = true
            socket.bind(InetSocketAddress(0))
            socket.connect(remoteAddress, remotePort)

            sendFrame(
                socket = socket,
                frame = handshake.createHelloFrame(),
                authenticationKey = null,
            )

            val deadline =
                System.nanoTime() +
                    timeoutMilliseconds.toLong() * 1_000_000L

            var pendingChallenge: ProtocolFrame? = null
            var proofSent = false

            while (System.nanoTime() < deadline) {
                val datagram = receiveDatagram(socket, deadline) ?: break

                val frame =
                    if (looksAuthenticated(datagram)) {
                        if (!proofSent) {
                            continue
                        }

                        val decodeKey =
                            handshake.sessionKeyForAuthenticatedDecode()

                        try {
                            runCatching {
                                ProtocolFrameCodec.decode(
                                    datagram,
                                    decodeKey,
                                )
                            }.getOrNull() ?: continue
                        } finally {
                            decodeKey.fill(0)
                        }
                    } else {
                        runCatching {
                            ProtocolFrameCodec.decode(datagram)
                        }.getOrNull() ?: continue
                    }

                when (frame.messageType) {
                    MessageType.HELLO -> {
                        handshake.handleWindowsHello(frame)

                        val buffered = pendingChallenge
                        if (buffered != null) {
                            val proof = handshake.handleChallenge(buffered)
                            sendFrame(socket, proof, null)
                            proofSent = true
                            pendingChallenge = null
                        }
                    }

                    MessageType.AUTH_CHALLENGE -> {
                        if (handshake.state ==
                            com.natsx.controller.core.protocol.TrustedReconnectClientState.HELLO_SENT
                        ) {
                            pendingChallenge = frame
                        } else {
                            val proof = handshake.handleChallenge(frame)
                            sendFrame(socket, proof, null)
                            proofSent = true
                        }
                    }

                    MessageType.SESSION_READY -> {
                        if (!proofSent) {
                            continue
                        }

                        val established = handshake.handleSessionReady(frame)

                        val result = EstablishedWifiSession(
                            remoteAddress = remoteAddress,
                            remotePort = remotePort,
                            trustedSession = EstablishedTrustedSession(
                                androidDeviceId = established.androidDeviceId,
                                windowsDeviceId = established.windowsDeviceId,
                                sessionId = established.sessionId,
                                sessionKey = established.sessionKey.copyOf(),
                                negotiatedCapabilities =
                                    established.negotiatedCapabilities,
                            ),
                        )

                        handshake.close()
                        return result
                    }

                    else -> Unit
                }
            }
        }

        handshake.close()
        throw SocketTimeoutException(
            "Trusted Wi-Fi session handshake did not complete before timeout.",
        )
    }

    fun close() {
        pairingRootKey.fill(0)
    }

    private fun sendFrame(
        socket: DatagramSocket,
        frame: ProtocolFrame,
        authenticationKey: ByteArray?,
    ) {
        val bytes =
            if (authenticationKey == null) {
                ProtocolFrameCodec.encode(frame)
            } else {
                ProtocolFrameCodec.encode(frame, authenticationKey)
            }

        socket.send(DatagramPacket(bytes, bytes.size))
    }

    private fun receiveDatagram(
        socket: DatagramSocket,
        deadlineNanos: Long,
    ): ByteArray? {
        val remainingNanos = deadlineNanos - System.nanoTime()
        if (remainingNanos <= 0) {
            return null
        }

        socket.soTimeout =
            (remainingNanos / 1_000_000L)
                .coerceAtLeast(1L)
                .coerceAtMost(Int.MAX_VALUE.toLong())
                .toInt()

        val buffer = ByteArray(MAX_HANDSHAKE_DATAGRAM)
        val packet = DatagramPacket(buffer, buffer.size)

        return try {
            socket.receive(packet)
            packet.data.copyOfRange(
                packet.offset,
                packet.offset + packet.length,
            )
        } catch (_: SocketTimeoutException) {
            null
        }
    }

    private fun looksAuthenticated(datagram: ByteArray): Boolean =
        datagram.size >= 8 &&
            (datagram[7].toInt() and FrameFlags.AUTHENTICATED) != 0

    companion object {
        const val DEFAULT_HANDSHAKE_TIMEOUT_MS = 2_500
        private const val MAX_HANDSHAKE_DATAGRAM = 1024
    }
}
