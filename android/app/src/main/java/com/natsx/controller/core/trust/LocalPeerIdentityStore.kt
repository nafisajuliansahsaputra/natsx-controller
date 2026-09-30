package com.natsx.controller.core.trust

import android.content.SharedPreferences
import com.natsx.controller.core.protocol.PeerId
import java.util.Base64

class LocalPeerIdentityStore(
    private val preferences: SharedPreferences,
) {
    fun getOrCreate(): PeerId {
        preferences.getString(KEY_LOCAL_PEER_ID, null)?.let { stored ->
            runCatching {
                val bytes = Base64.getUrlDecoder().decode(stored)
                return PeerId.fromBytes(bytes)
            }
        }

        val created = PeerId.createRandom()
        val encoded =
            Base64.getUrlEncoder()
                .withoutPadding()
                .encodeToString(created.toByteArray())

        check(
            preferences.edit()
                .putString(KEY_LOCAL_PEER_ID, encoded)
                .commit(),
        ) {
            "Failed to persist local Peer ID."
        }

        return created
    }

    private companion object {
        const val KEY_LOCAL_PEER_ID = "local_peer_id_v1"
    }
}
