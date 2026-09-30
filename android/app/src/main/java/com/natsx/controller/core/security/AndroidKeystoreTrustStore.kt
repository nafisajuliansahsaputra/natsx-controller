package com.natsx.controller.core.security

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import com.natsx.controller.core.protocol.PeerId
import java.security.KeyStore
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class AndroidKeystoreTrustedPeerStore(
    context: Context,
) : TrustedPeerStore {
    private val preferences =
        context.applicationContext.getSharedPreferences(
            PREFERENCES_NAME,
            Context.MODE_PRIVATE,
        )

    override fun saveTrustKey(
        peerId: PeerId,
        trustKey: ByteArray,
    ) {
        require(trustKey.size == TRUST_KEY_SIZE) {
            "Trust key must be exactly $TRUST_KEY_SIZE bytes."
        }

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateMasterKey())
        cipher.updateAAD(peerId.toByteArray())

        val encrypted = cipher.doFinal(trustKey)
        val iv = cipher.iv

        require(iv.size == IV_SIZE) {
            "Unexpected Android Keystore GCM IV length."
        }

        val record = ByteArray(1 + IV_SIZE + encrypted.size)
        record[0] = FORMAT_VERSION
        iv.copyInto(record, 1)
        encrypted.copyInto(record, 1 + IV_SIZE)

        preferences.edit()
            .putString(storageKey(peerId), Base64.encodeToString(record, Base64.NO_WRAP))
            .apply()

        record.fill(0)
        encrypted.fill(0)
    }

    override fun getTrustKey(peerId: PeerId): ByteArray? {
        val encoded = preferences.getString(storageKey(peerId), null) ?: return null
        val record = Base64.decode(encoded, Base64.NO_WRAP)

        try {
            require(record.size > 1 + IV_SIZE + 16) {
                "Stored trust record is too short."
            }
            require(record[0] == FORMAT_VERSION) {
                "Unsupported trust-record format."
            }

            val iv = record.copyOfRange(1, 1 + IV_SIZE)
            val encrypted = record.copyOfRange(1 + IV_SIZE, record.size)

            try {
                val cipher = Cipher.getInstance(TRANSFORMATION)
                cipher.init(
                    Cipher.DECRYPT_MODE,
                    getOrCreateMasterKey(),
                    GCMParameterSpec(GCM_TAG_BITS, iv),
                )
                cipher.updateAAD(peerId.toByteArray())

                return cipher.doFinal(encrypted).also { trustKey ->
                    require(trustKey.size == TRUST_KEY_SIZE) {
                        trustKey.fill(0)
                        "Stored trust key has an invalid length."
                    }
                }
            } finally {
                iv.fill(0)
                encrypted.fill(0)
            }
        } finally {
            record.fill(0)
        }
    }

    override fun contains(peerId: PeerId): Boolean =
        preferences.contains(storageKey(peerId))

    override fun remove(peerId: PeerId): Boolean {
        val key = storageKey(peerId)
        val existed = preferences.contains(key)
        preferences.edit().remove(key).apply()
        return existed
    }

    private fun getOrCreateMasterKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply {
            load(null)
        }

        val existing = keyStore.getKey(MASTER_KEY_ALIAS, null)
        if (existing is SecretKey) {
            return existing
        }

        return KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            ANDROID_KEYSTORE,
        ).run {
            init(
                KeyGenParameterSpec.Builder(
                    MASTER_KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                )
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build(),
            )
            generateKey()
        }
    }

    private fun storageKey(peerId: PeerId): String =
        "peer_" + peerId.toString()

    companion object {
        private const val TRUST_KEY_SIZE = 32
        private const val FORMAT_VERSION: Byte = 1
        private const val IV_SIZE = 12
        private const val GCM_TAG_BITS = 128
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val MASTER_KEY_ALIAS = "natsx_controller_trust_master_v1"
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val PREFERENCES_NAME = "natsx_controller_trust"
    }
}

class AndroidLocalPeerIdentityStore(
    context: Context,
) : LocalPeerIdentityStore {
    private val preferences =
        context.applicationContext.getSharedPreferences(
            PREFERENCES_NAME,
            Context.MODE_PRIVATE,
        )

    @Synchronized
    override fun getOrCreate(): PeerId {
        val existing = preferences.getString(PEER_ID_KEY, null)

        if (existing != null) {
            val bytes = Base64.decode(existing, Base64.NO_WRAP)
            require(bytes.size == PeerId.SIZE) {
                "Stored Android controller Peer ID has an invalid length."
            }
            return PeerId.fromBytes(bytes)
        }

        val peerId = PeerId.createRandom()
        val bytes = peerId.toByteArray()

        preferences.edit()
            .putString(
                PEER_ID_KEY,
                Base64.encodeToString(bytes, Base64.NO_WRAP),
            )
            .commit()

        return peerId
    }

    companion object {
        private const val PREFERENCES_NAME = "natsx_controller_identity"
        private const val PEER_ID_KEY = "controller_peer_id"
    }
}
