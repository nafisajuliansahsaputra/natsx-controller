package com.natsx.controller.transport.wifi

import com.natsx.controller.core.protocol.DiscoveryCodec
import com.natsx.controller.core.protocol.DiscoveryRequest
import com.natsx.controller.core.protocol.DiscoveryResponse
import com.natsx.controller.core.protocol.SessionId
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

data class DiscoveredReceiver(
    val address: InetAddress,
    val response: DiscoveryResponse,
)

class WifiDiscoveryClient(
    private val androidDeviceId: SessionId,
) {
    fun discover(timeoutMilliseconds: Int = 800): List<DiscoveredReceiver> {
        require(timeoutMilliseconds in 100..5000)

        val request = DiscoveryCodec.encodeRequest(
            DiscoveryRequest(androidDeviceId),
        )
        val results = linkedMapOf<String, DiscoveredReceiver>()

        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = timeoutMilliseconds

            for (broadcast in broadcastAddresses()) {
                runCatching {
                    socket.send(
                        DatagramPacket(
                            request,
                            request.size,
                            broadcast,
                            DISCOVERY_PORT,
                        ),
                    )
                }
            }

            val deadline = System.nanoTime() + timeoutMilliseconds * 1_000_000L
            val buffer = ByteArray(MAX_DISCOVERY_PACKET)

            while (System.nanoTime() < deadline) {
                val remainingMillis =
                    ((deadline - System.nanoTime()) / 1_000_000L)
                        .coerceAtLeast(1L)
                        .coerceAtMost(Int.MAX_VALUE.toLong())
                        .toInt()

                socket.soTimeout = remainingMillis

                val packet = DatagramPacket(buffer, buffer.size)

                try {
                    socket.receive(packet)
                } catch (_: SocketTimeoutException) {
                    break
                }

                val bytes = packet.data.copyOfRange(
                    packet.offset,
                    packet.offset + packet.length,
                )

                val response = runCatching {
                    DiscoveryCodec.decodeResponse(bytes)
                }.getOrNull() ?: continue

                val key =
                    packet.address.hostAddress +
                        ":" +
                        response.controllerPort +
                        ":" +
                        response.windowsDeviceId.toString()

                results[key] = DiscoveredReceiver(packet.address, response)
            }
        }

        return results.values.toList()
    }

    private fun broadcastAddresses(): Set<InetAddress> {
        val addresses = linkedSetOf<InetAddress>()
        addresses += InetAddress.getByName("255.255.255.255")

        val interfaces = NetworkInterface.getNetworkInterfaces() ?: return addresses

        for (networkInterface in interfaces.toList()) {
            if (!runCatching { networkInterface.isUp }.getOrDefault(false) ||
                networkInterface.isLoopback
            ) {
                continue
            }

            for (interfaceAddress in networkInterface.interfaceAddresses) {
                val broadcast = interfaceAddress.broadcast
                if (broadcast is Inet4Address) {
                    addresses += broadcast
                }
            }
        }

        return addresses
    }

    companion object {
        const val DISCOVERY_PORT = 37073
        private const val MAX_DISCOVERY_PACKET = 256
    }
}
