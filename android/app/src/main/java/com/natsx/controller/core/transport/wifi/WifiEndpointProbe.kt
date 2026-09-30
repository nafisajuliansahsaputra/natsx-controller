package com.natsx.controller.core.transport.wifi

import android.os.SystemClock
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException

fun interface WifiEndpointProbe {
    fun isReachable(endpoint: InetSocketAddress): Boolean
}

/**
 * Verifies a cached receiver endpoint using the already-trusted session key.
 *
 * A positive result means the endpoint returned a valid authenticated
 * HEARTBEAT_ACK echo for this session. Discovery is not involved.
 */
class AuthenticatedWifiEndpointProbe(
    private val trustedSession: WifiTrustedSession,
    private val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL
    },
) : WifiEndpointProbe {
    init {
        require(timeoutMillis in 50..5_000)
    }

    override fun isReachable(endpoint: InetSocketAddress): Boolean {
        val probeTimestamp = monotonicMicros()
        val heartbeat = WifiControlDatagramCodec.encodeHeartbeat(
            trustedSession = trustedSession,
            monotonicTimestampMicros = probeTimestamp,
        )

        return try {
            DatagramSocket().use { socket ->
                socket.connect(endpoint)
                socket.soTimeout = timeoutMillis

                socket.send(
                    DatagramPacket(
                        heartbeat,
                        heartbeat.size,
                        endpoint,
                    ),
                )

                val buffer = ByteArray(MAXIMUM_DATAGRAM_SIZE)
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)

                val response = packet.data.copyOfRange(
                    packet.offset,
                    packet.offset + packet.length,
                )

                WifiControlDatagramCodec.decodeHeartbeatAck(
                    datagram = response,
                    trustedSession = trustedSession,
                ) == probeTimestamp
            }
        } catch (_: SocketTimeoutException) {
            false
        } catch (_: IOException) {
            false
        } catch (_: IllegalArgumentException) {
            false
        } catch (_: IllegalStateException) {
            false
        }
    }

    companion object {
        const val DEFAULT_TIMEOUT_MILLIS = 350
        const val MAXIMUM_DATAGRAM_SIZE = 512
    }
}
