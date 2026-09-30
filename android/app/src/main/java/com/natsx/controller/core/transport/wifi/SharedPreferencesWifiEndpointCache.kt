package com.natsx.controller.core.transport.wifi

import android.content.SharedPreferences
import com.natsx.controller.core.protocol.PeerId
import java.net.InetSocketAddress

/**
 * Small local cache for last-known trusted Wi-Fi receiver endpoints.
 *
 * This stores only routing metadata (host/port), never trust/session keys.
 * Cached endpoints are always revalidated with an authenticated heartbeat
 * before reuse.
 */
class SharedPreferencesWifiEndpointCache(
    private val preferences: SharedPreferences,
) : WifiEndpointCache {
    override fun get(receiverPeerId: PeerId): InetSocketAddress? {
        val prefix = keyPrefix(receiverPeerId)
        val host = preferences.getString(prefix + HOST_SUFFIX, null)
            ?.takeIf { it.isNotBlank() }
            ?: return null
        val port = preferences.getInt(prefix + PORT_SUFFIX, 0)

        if (port !in 1..65535) {
            remove(receiverPeerId)
            return null
        }

        return InetSocketAddress(host, port)
    }

    override fun put(
        receiverPeerId: PeerId,
        endpoint: InetSocketAddress,
    ) {
        require(endpoint.port in 1..65535)

        preferences.edit()
            .putString(keyPrefix(receiverPeerId) + HOST_SUFFIX, endpoint.hostString)
            .putInt(keyPrefix(receiverPeerId) + PORT_SUFFIX, endpoint.port)
            .apply()
    }

    override fun remove(receiverPeerId: PeerId) {
        val prefix = keyPrefix(receiverPeerId)

        preferences.edit()
            .remove(prefix + HOST_SUFFIX)
            .remove(prefix + PORT_SUFFIX)
            .apply()
    }

    private fun keyPrefix(receiverPeerId: PeerId): String =
        "wifi_endpoint_" + receiverPeerId.toString()

    private companion object {
        const val HOST_SUFFIX = "_host"
        const val PORT_SUFFIX = "_port"
    }
}
