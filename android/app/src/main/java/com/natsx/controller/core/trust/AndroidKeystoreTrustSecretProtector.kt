package com.natsx.controller.core.trust

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import com.natsx.controller.core.protocol.PeerId
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class AndroidKeystoreTrustSecretProtector(
    private val keyAlias: String = DEFAULT_KEY_ALIAS,
) : TrustSecretProtector {
    override fun protect(
        peerId: PeerId,
        plaintext: ByteArray,
    ): ProtectedTrustSecret {
        require(plaintext.size == TrustedPeerMaterial.TRUST_SECRET_SIZE) {
            "Trust secret must be exactly 32 bytes."
        }

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        cipher.updateAAD(aad(peerId))

        return ProtectedTrustSecret(
            iv = cipher.iv.copyOf(),
            ciphertext = cipher.doFinal(plaintext),
        )
    }

    override fun unprotect(
        peerId: PeerId,
        protectedSecret: ProtectedTrustSecret,
    ): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(
            Cipher.DECRYPT_MODE,
            getOrCreateKey(),
            GCMParameterSpec(TAG_LENGTH_BITS, protectedSecret.iv),
        )
        cipher.updateAAD(aad(peerId))

        val plaintext = cipher.doFinal(protectedSecret.ciphertext)

        require(plaintext.size == TrustedPeerMaterial.TRUST_SECRET_SIZE) {
            "Decrypted trust secret has an invalid length."
        }

        return plaintext
    }

    private fun getOrCreateKey(): SecretKey {
        val keyStore =
            KeyStore.getInstance(ANDROID_KEYSTORE).apply {
                load(null)
            }

        (keyStore.getKey(keyAlias, null) as? SecretKey)?.let {
            return it
        }

        val generator =
            KeyGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_AES,
                ANDROID_KEYSTORE,
            )

        generator.init(
            KeyGenParameterSpec.Builder(
                keyAlias,
                KeyProperties.PURPOSE_ENCRYPT or
                    KeyProperties.PURPOSE_DECRYPT,
            )
                .setKeySize(KEY_SIZE_BITS)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(
                    KeyProperties.ENCRYPTION_PADDING_NONE,
                )
                .setRandomizedEncryptionRequired(true)
                .build(),
        )

        return generator.generateKey()
    }

    private fun aad(peerId: PeerId): ByteArray =
        AAD_CONTEXT.toByteArray(StandardCharsets.US_ASCII) +
            peerId.toByteArray()

    companion object {
        const val DEFAULT_KEY_ALIAS = "natsx.controller.trust.v1"
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val KEY_SIZE_BITS = 256
        private const val TAG_LENGTH_BITS = 128
        private const val AAD_CONTEXT = "NATSX-TRUST-STORE-V1"
    }
}
