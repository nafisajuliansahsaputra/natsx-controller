package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId
import java.net.InetSocketAddress

enum class WifiEndpointResolutionSource {
    CACHED_DIRECT,
    DISCOVERY,
}

data class WifiResolvedEndpoint(
    val endpoint: InetSocketAddress,
    val source: WifiEndpointResolutionSource,
)

/**
 * Resolves the trusted Windows receiver endpoint using the fast path first.
 *
 * 1. Try the last known endpoint with an authenticated direct probe.
 * 2. If that fails, discard the stale cache entry.
 * 3. Fall back to LAN discovery constrained to the trusted receiver Peer ID.
 * 4. Cache a successful discovery result for the next reconnect.
 */
fun interface WifiEndpointProvider {
    fun resolve(): WifiResolvedEndpoint?
}

class WifiEndpointResolver(
    private val receiverPeerId: PeerId,
    private val endpointCache: WifiEndpointCache,
    private val endpointProbe: WifiEndpointProbe,
    private val discovery: WifiReceiverDiscovery,
) : WifiEndpointProvider {
    override fun resolve(): WifiResolvedEndpoint? {
        endpointCache.get(receiverPeerId)?.let { cached ->
            if (endpointProbe.isReachable(cached)) {
                return WifiResolvedEndpoint(
                    endpoint = cached,
                    source = WifiEndpointResolutionSource.CACHED_DIRECT,
                )
            }

            endpointCache.remove(receiverPeerId)
        }

        val discovered = discovery.discover(receiverPeerId) ?: return null

        if (discovered.peerId != receiverPeerId) {
            return null
        }

        endpointCache.put(receiverPeerId, discovered.endpoint)

        return WifiResolvedEndpoint(
            endpoint = discovered.endpoint,
            source = WifiEndpointResolutionSource.DISCOVERY,
        )
    }
}
