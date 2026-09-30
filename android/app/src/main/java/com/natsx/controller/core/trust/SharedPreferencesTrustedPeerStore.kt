package com.natsx.controller.core.trust

import android.content.SharedPreferences
import com.natsx.controller.core.protocol.PeerId
import java.util.Base64

class SharedPreferencesTrustedPeerStore(
    private val preferences: SharedPreferences,
    private val protector: TrustSecretProtector,
) : TrustedPeerStore {
    override fun put(
        record: TrustedPeerRecord,
        trustSecret: ByteArray,
    ) {
        require(trustSecret.size == TrustedPeerMaterial.TRUST_SECRET_SIZE) {
            "Trust secret must be exactly 32 bytes."
        }

        val protectedSecret = protector.protect(record.peerId, trustSecret)
        val id = encode(record.peerId.toByteArray())
        val peers =
            preferences.getStringSet(KEY_PEERS, emptySet())
                ?.toMutableSet()
                ?: mutableSetOf()

        peers += id

        preferences.edit()
            .putStringSet(KEY_PEERS, peers)
            .putString(key(id, "name"), record.displayName)
            .putInt(key(id, "capabilities"), record.capabilities)
            .putLong(key(id, "pairedAt"), record.pairedAtEpochMillis)
            .putInt(key(id, "pairingVersion"), record.pairingVersion)
            .putString(key(id, "iv"), encode(protectedSecret.iv))
            .putString(key(id, "ciphertext"), encode(protectedSecret.ciphertext))
            .apply()
    }

    override fun get(peerId: PeerId): TrustedPeerMaterial? {
        val id = encode(peerId.toByteArray())
        if (!containsPeer(id)) return null

        val iv =
            preferences.getString(key(id, "iv"), null)
                ?.let(::decode)
                ?: return null
        val ciphertext =
            preferences.getString(key(id, "ciphertext"), null)
                ?.let(::decode)
                ?: return null
        val record = readRecord(id, peerId) ?: return null
        val plaintext =
            protector.unprotect(
                peerId,
                ProtectedTrustSecret(iv, ciphertext),
            )

        return try {
            TrustedPeerMaterial(record, plaintext)
        } finally {
            plaintext.fill(0)
        }
    }

    override fun list(): List<TrustedPeerRecord> =
        preferences.getStringSet(KEY_PEERS, emptySet())
            .orEmpty()
            .mapNotNull { id ->
                runCatching {
                    val peerId = PeerId.fromBytes(decode(id))
                    readRecord(id, peerId)
                }.getOrNull()
            }
            .sortedByDescending { it.pairedAtEpochMillis }

    override fun remove(peerId: PeerId) {
        val id = encode(peerId.toByteArray())
        val peers =
            preferences.getStringSet(KEY_PEERS, emptySet())
                ?.toMutableSet()
                ?: mutableSetOf()

        peers.remove(id)

        preferences.edit()
            .putStringSet(KEY_PEERS, peers)
            .remove(key(id, "name"))
            .remove(key(id, "capabilities"))
            .remove(key(id, "pairedAt"))
            .remove(key(id, "pairingVersion"))
            .remove(key(id, "iv"))
            .remove(key(id, "ciphertext"))
            .apply()
    }

    override fun clear() {
        list().forEach { remove(it.peerId) }
    }

    private fun readRecord(
        id: String,
        peerId: PeerId,
    ): TrustedPeerRecord? {
        if (!preferences.contains(key(id, "pairedAt"))) return null

        return TrustedPeerRecord(
            peerId = peerId,
            displayName = preferences.getString(key(id, "name"), null),
            capabilities = preferences.getInt(key(id, "capabilities"), 0),
            pairedAtEpochMillis = preferences.getLong(key(id, "pairedAt"), 0),
            pairingVersion =
                preferences.getInt(
                    key(id, "pairingVersion"),
                    TrustedPeerRecord.CURRENT_PAIRING_VERSION,
                ),
        )
    }

    private fun containsPeer(id: String): Boolean =
        preferences.getStringSet(KEY_PEERS, emptySet())
            .orEmpty()
            .contains(id)

    private fun key(
        id: String,
        field: String,
    ): String = "peer_" + id + "_" + field

    private fun encode(bytes: ByteArray): String =
        Base64.getUrlEncoder()
            .withoutPadding()
            .encodeToString(bytes)

    private fun decode(text: String): ByteArray =
        Base64.getUrlDecoder().decode(text)

    private companion object {
        const val KEY_PEERS = "trusted_peer_ids"
    }
}
