package com.natsx.controller.security

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedSessionCrypto
import java.nio.ByteBuffer
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class TrustedReceiverRecord(
    val windowsDeviceId: SessionId,
    val pairingRootKey: ByteArray,
    val displayName: String?,
    val lastHostAddress: String?,
    val lastPort: Int,
)

class AndroidTrustedReceiverStore(
    context: Context,
) {
    private val preferences =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun save(
        windowsDeviceId: SessionId,
        pairingRootKey: ByteArray,
        displayName: String? = null,
        lastHostAddress: String? = null,
        lastPort: Int = DEFAULT_CONTROLLER_PORT,
    ) {
        require(windowsDeviceId != SessionId.Zero)
        require(pairingRootKey.size == TrustedSessionCrypto.PAIRING_ROOT_KEY_SIZE)
        require(lastPort in 1..65535)

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateSecretKey())

        val encrypted = cipher.doFinal(pairingRootKey)
        val iv = cipher.iv

        val packed = ByteBuffer
            .allocate(1 + iv.size + encrypted.size)
            .put(iv.size.toByte())
            .put(iv)
            .put(encrypted)
            .array()

        val prefix = keyPrefix(windowsDeviceId)

        val trustedIds =
            preferences.getStringSet(KEY_TRUSTED_IDS, emptySet())
                .orEmpty()
                .toMutableSet()
                .apply {
                    add(windowsDeviceId.toString())
                }

        preferences.edit()
            .putStringSet(KEY_TRUSTED_IDS, trustedIds)
            .putString(
                prefix + KEY_SECRET_SUFFIX,
                Base64.encodeToString(packed, Base64.NO_WRAP),
            )
            .putString(prefix + KEY_NAME_SUFFIX, displayName)
            .putString(prefix + KEY_HOST_SUFFIX, lastHostAddress)
            .putInt(prefix + KEY_PORT_SUFFIX, lastPort)
            .apply()

        encrypted.fill(0)
        packed.fill(0)
    }

    fun tryGet(windowsDeviceId: SessionId): TrustedReceiverRecord? {
        val prefix = keyPrefix(windowsDeviceId)
        val encoded =
            preferences.getString(prefix + KEY_SECRET_SUFFIX, null)
                ?: return null

        val packed = Base64.decode(encoded, Base64.NO_WRAP)
        require(packed.isNotEmpty()) {
            "Stored trusted receiver payload is invalid."
        }

        val ivLength = packed[0].toInt() and 0xFF
        require(ivLength in 12..32)
        require(packed.size > 1 + ivLength)

        val iv = packed.copyOfRange(1, 1 + ivLength)
        val encrypted = packed.copyOfRange(1 + ivLength, packed.size)

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(
            Cipher.DECRYPT_MODE,
            getOrCreateSecretKey(),
            GCMParameterSpec(GCM_TAG_BITS, iv),
        )

        val rootKey = cipher.doFinal(encrypted)

        packed.fill(0)
        encrypted.fill(0)
        iv.fill(0)

        require(rootKey.size == TrustedSessionCrypto.PAIRING_ROOT_KEY_SIZE) {
            rootKey.fill(0)
            "Stored trusted receiver root key is invalid."
        }

        return TrustedReceiverRecord(
            windowsDeviceId = windowsDeviceId,
            pairingRootKey = rootKey,
            displayName =
                preferences.getString(prefix + KEY_NAME_SUFFIX, null),
            lastHostAddress =
                preferences.getString(prefix + KEY_HOST_SUFFIX, null),
            lastPort =
                preferences.getInt(
                    prefix + KEY_PORT_SUFFIX,
                    DEFAULT_CONTROLLER_PORT,
                ),
        )
    }

    fun updateEndpoint(
        windowsDeviceId: SessionId,
        displayName: String?,
        hostAddress: String,
        port: Int,
    ) {
        require(port in 1..65535)
        val prefix = keyPrefix(windowsDeviceId)

        if (!preferences.contains(prefix + KEY_SECRET_SUFFIX)) {
            return
        }

        preferences.edit()
            .putString(prefix + KEY_NAME_SUFFIX, displayName)
            .putString(prefix + KEY_HOST_SUFFIX, hostAddress)
            .putInt(prefix + KEY_PORT_SUFFIX, port)
            .apply()
    }

    fun listTrustedDeviceIds(): List<SessionId> {
        return preferences
            .getStringSet(KEY_TRUSTED_IDS, emptySet())
            .orEmpty()
            .mapNotNull { encoded ->
                runCatching {
                    SessionId.fromBytes(hexToBytes(encoded))
                }.getOrNull()
            }
    }

    fun forget(windowsDeviceId: SessionId): Boolean {
        val prefix = keyPrefix(windowsDeviceId)
        val secretKey = prefix + KEY_SECRET_SUFFIX

        if (!preferences.contains(secretKey)) {
            return false
        }

        val trustedIds =
            preferences.getStringSet(KEY_TRUSTED_IDS, emptySet())
                .orEmpty()
                .toMutableSet()
                .apply {
                    remove(windowsDeviceId.toString())
                }

        preferences.edit()
            .putStringSet(KEY_TRUSTED_IDS, trustedIds)
            .remove(secretKey)
            .remove(prefix + KEY_NAME_SUFFIX)
            .remove(prefix + KEY_HOST_SUFFIX)
            .remove(prefix + KEY_PORT_SUFFIX)
            .apply()

        return true
    }

    private fun getOrCreateSecretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEY_STORE).apply {
            load(null)
        }

        val existing = keyStore.getKey(KEY_ALIAS, null) as? SecretKey
        if (existing != null) {
            return existing
        }

        val generator = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            ANDROID_KEY_STORE,
        )

        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or
                    KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(
                    KeyProperties.ENCRYPTION_PADDING_NONE,
                )
                .setKeySize(256)
                .build(),
        )

        return generator.generateKey()
    }

    private fun keyPrefix(deviceId: SessionId): String =
        "peer_" + deviceId.toString() + "_"

    private fun hexToBytes(value: String): ByteArray {
        require(value.length == SessionId.SIZE * 2)

        return ByteArray(SessionId.SIZE) { index ->
            value.substring(index * 2, index * 2 + 2)
                .toInt(16)
                .toByte()
        }
    }

    private companion object {
        const val PREFERENCES_NAME = "natsx_trusted_receivers"
        const val ANDROID_KEY_STORE = "AndroidKeyStore"
        const val KEY_ALIAS = "natsx-controller-trust-aes-v1"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val GCM_TAG_BITS = 128
        const val DEFAULT_CONTROLLER_PORT = 37074

        const val KEY_TRUSTED_IDS = "trusted_ids"
        const val KEY_SECRET_SUFFIX = "secret"
        const val KEY_NAME_SUFFIX = "name"
        const val KEY_HOST_SUFFIX = "host"
        const val KEY_PORT_SUFFIX = "port"
    }
}
