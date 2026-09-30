package com.natsx.controller.core.transport.wifi

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException

data class DiscoveredWifiReceiver(
    val receiverId: ByteArray,
    val receiverName: String,
    val endpoint: InetSocketAddress,
) {
    override fun equals(other: Any?): Boolean =
        other is DiscoveredWifiReceiver &&
            receiverId.contentEquals(other.receiverId) &&
            receiverName == other.receiverName &&
            endpoint == other.endpoint

    override fun hashCode(): Int {
        var result = receiverId.contentHashCode()
        result = 31 * result + receiverName.hashCode()
        result = 31 * result + endpoint.hashCode()
        return result
    }
}

class WifiDiscoveryClient(
    private val discoveryPort: Int = WifiDiscoveryProtocol.DISCOVERY_PORT,
) {
    fun discover(timeoutMillis: Int = 700): List<DiscoveredWifiReceiver> {
        require(timeoutMillis > 0)

        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = timeoutMillis

            val request = WifiDiscoveryProtocol.createRequest()
            val packet = DatagramPacket(
                request,
                request.size,
                InetAddress.getByName("255.255.255.255"),
                discoveryPort,
            )
            socket.send(packet)

            val deadlineNanos =
                System.nanoTime() + timeoutMillis * 1_000_000L

            val discovered = LinkedHashMap<String, DiscoveredWifiReceiver>()

            while (System.nanoTime() < deadlineNanos) {
                val remainingMillis =
                    ((deadlineNanos - System.nanoTime()) / 1_000_000L)
                        .coerceAtLeast(1L)
                        .coerceAtMost(Int.MAX_VALUE.toLong())
                        .toInt()

                socket.soTimeout = remainingMillis

                val buffer = ByteArray(256)
                val responsePacket = DatagramPacket(buffer, buffer.size)

                try {
                    socket.receive(responsePacket)
                } catch (_: SocketTimeoutException) {
                    break
                }

                val responseBytes =
                    responsePacket.data.copyOfRange(
                        responsePacket.offset,
                        responsePacket.offset + responsePacket.length,
                    )

                val response = runCatching {
                    WifiDiscoveryProtocol.decodeResponse(responseBytes)
                }.getOrNull() ?: continue

                val endpoint = InetSocketAddress(
                    responsePacket.address,
                    response.realtimePort,
                )

                val idKey = response.receiverId.joinToString(separator = "") {
                    "%02x".format(it.toInt() and 0xFF)
                }

                discovered[idKey] = DiscoveredWifiReceiver(
                    receiverId = response.receiverId.copyOf(),
                    receiverName = response.receiverName,
                    endpoint = endpoint,
                )
            }

            return discovered.values.toList()
        }
    }
}
