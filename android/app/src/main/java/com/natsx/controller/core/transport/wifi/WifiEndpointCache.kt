package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId
import java.net.InetSocketAddress
import java.util.concurrent.ConcurrentHashMap

interface WifiEndpointCache {
    fun get(receiverPeerId: PeerId): InetSocketAddress?

    fun put(
        receiverPeerId: PeerId,
        endpoint: InetSocketAddress,
    )

    fun remove(receiverPeerId: PeerId)
}

class InMemoryWifiEndpointCache : WifiEndpointCache {
    private val endpoints = ConcurrentHashMap<PeerId, InetSocketAddress>()

    override fun get(receiverPeerId: PeerId): InetSocketAddress? =
        endpoints[receiverPeerId]

    override fun put(
        receiverPeerId: PeerId,
        endpoint: InetSocketAddress,
    ) {
        endpoints[receiverPeerId] = endpoint
    }

    override fun remove(receiverPeerId: PeerId) {
        endpoints.remove(receiverPeerId)
    }
}
