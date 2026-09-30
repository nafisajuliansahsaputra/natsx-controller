package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.net.InetAddress
import java.net.InetSocketAddress

class WifiEndpointResolverTest {
    private val receiverPeerId =
        PeerId.fromBytes(ByteArray(PeerId.SIZE) { (it + 1).toByte() })

    @Test
    fun usesAuthenticatedCachedEndpointBeforeDiscovery() {
        val cached = endpoint("192.168.1.10", 43860)
        val cache = InMemoryWifiEndpointCache().apply {
            put(receiverPeerId, cached)
        }

        var discoveryCalls = 0
        val resolver = WifiEndpointResolver(
            receiverPeerId = receiverPeerId,
            endpointCache = cache,
            endpointProbe = WifiEndpointProbe { it == cached },
            discovery = WifiReceiverDiscovery {
                discoveryCalls += 1
                null
            },
        )

        val resolved = resolver.resolve()

        assertEquals(cached, resolved?.endpoint)
        assertEquals(
            WifiEndpointResolutionSource.CACHED_DIRECT,
            resolved?.source,
        )
        assertEquals(0, discoveryCalls)
    }

    @Test
    fun staleCachedEndpointFallsBackToTrustedPeerDiscoveryAndRefreshesCache() {
        val stale = endpoint("192.168.1.10", 43860)
        val discoveredEndpoint = endpoint("192.168.1.25", 43860)
        val cache = InMemoryWifiEndpointCache().apply {
            put(receiverPeerId, stale)
        }

        val resolver = WifiEndpointResolver(
            receiverPeerId = receiverPeerId,
            endpointCache = cache,
            endpointProbe = WifiEndpointProbe { false },
            discovery = WifiReceiverDiscovery { expectedPeerId ->
                assertEquals(receiverPeerId, expectedPeerId)
                WifiDiscoveredReceiver(
                    peerId = receiverPeerId,
                    endpoint = discoveredEndpoint,
                    capabilities = 1,
                )
            },
        )

        val resolved = resolver.resolve()

        assertEquals(discoveredEndpoint, resolved?.endpoint)
        assertEquals(
            WifiEndpointResolutionSource.DISCOVERY,
            resolved?.source,
        )
        assertEquals(discoveredEndpoint, cache.get(receiverPeerId))
    }

    @Test
    fun failedCacheAndDiscoveryReturnsNullAndRemovesStaleEndpoint() {
        val stale = endpoint("192.168.1.10", 43860)
        val cache = InMemoryWifiEndpointCache().apply {
            put(receiverPeerId, stale)
        }

        val resolver = WifiEndpointResolver(
            receiverPeerId = receiverPeerId,
            endpointCache = cache,
            endpointProbe = WifiEndpointProbe { false },
            discovery = WifiReceiverDiscovery { null },
        )

        assertNull(resolver.resolve())
        assertNull(cache.get(receiverPeerId))
    }

    @Test
    fun rejectsDiscoveryResultForDifferentPeerEvenIfDiscoveryImplementationMisbehaves() {
        val otherPeer =
            PeerId.fromBytes(ByteArray(PeerId.SIZE) { (it + 33).toByte() })
        val cache = InMemoryWifiEndpointCache()
        val discoveredEndpoint = endpoint("192.168.1.25", 43860)

        val resolver = WifiEndpointResolver(
            receiverPeerId = receiverPeerId,
            endpointCache = cache,
            endpointProbe = WifiEndpointProbe { false },
            discovery = WifiReceiverDiscovery {
                WifiDiscoveredReceiver(
                    peerId = otherPeer,
                    endpoint = discoveredEndpoint,
                    capabilities = 1,
                )
            },
        )

        assertNull(resolver.resolve())
        assertNull(cache.get(receiverPeerId))
    }

    private fun endpoint(
        address: String,
        port: Int,
    ): InetSocketAddress =
        InetSocketAddress(InetAddress.getByName(address), port)
}
