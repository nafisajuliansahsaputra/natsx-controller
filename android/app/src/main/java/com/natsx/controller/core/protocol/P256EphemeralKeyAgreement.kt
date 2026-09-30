package com.natsx.controller.core.protocol

import java.math.BigInteger
import java.security.AlgorithmParameters
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.PrivateKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import javax.crypto.KeyAgreement
import javax.security.auth.Destroyable

class P256EphemeralKeyAgreement : AutoCloseable {
    private val keyPair =
        KeyPairGenerator.getInstance("EC")
            .apply {
                initialize(ECGenParameterSpec(CURVE_NAME))
            }
            .generateKeyPair()

    private var closed = false

    fun exportPublicKey(): ByteArray {
        check(!closed) { "P-256 key agreement is closed." }

        val publicKey =
            keyPair.public as? ECPublicKey
                ?: error("Provider did not return an EC public key.")

        val x = unsignedFixed(publicKey.w.affineX, COORDINATE_SIZE)
        val y = unsignedFixed(publicKey.w.affineY, COORDINATE_SIZE)

        return ByteArray(
            PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE,
        ).also { encoded ->
            encoded[0] = 0x04
            x.copyInto(encoded, destinationOffset = 1)
            y.copyInto(encoded, destinationOffset = 33)
        }
    }

    fun deriveSharedSecret(remotePublicKey: ByteArray): ByteArray {
        check(!closed) { "P-256 key agreement is closed." }
        require(
            remotePublicKey.size ==
                PairingCrypto.P256_UNCOMPRESSED_PUBLIC_KEY_SIZE &&
                remotePublicKey[0] == 0x04.toByte(),
        ) {
            "P-256 public key must use 65-byte uncompressed SEC1 encoding."
        }

        val x =
            BigInteger(
                1,
                remotePublicKey.copyOfRange(1, 33),
            )
        val y =
            BigInteger(
                1,
                remotePublicKey.copyOfRange(33, 65),
            )

        val remote =
            KeyFactory.getInstance("EC")
                .generatePublic(
                    ECPublicKeySpec(
                        ECPoint(x, y),
                        p256Parameters(),
                    ),
                )

        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(keyPair.private)
        agreement.doPhase(remote, true)

        val secret = agreement.generateSecret()

        require(secret.size == PairingCrypto.DERIVED_KEY_SIZE) {
            secret.fill(0)
            "P-256 ECDH shared secret must be exactly 32 bytes."
        }

        return secret
    }

    override fun close() {
        if (closed) return

        destroyIfSupported(keyPair.private)
        closed = true
    }

    private fun destroyIfSupported(privateKey: PrivateKey) {
        (privateKey as? Destroyable)?.let { destroyable ->
            runCatching {
                destroyable.destroy()
            }
        }
    }

    private fun p256Parameters(): ECParameterSpec {
        val parameters = AlgorithmParameters.getInstance("EC")
        parameters.init(ECGenParameterSpec(CURVE_NAME))
        return parameters.getParameterSpec(ECParameterSpec::class.java)
    }

    private fun unsignedFixed(
        value: BigInteger,
        size: Int,
    ): ByteArray {
        val raw = value.toByteArray()
        val unsigned =
            if (raw.size > size && raw[0] == 0.toByte()) {
                raw.copyOfRange(1, raw.size)
            } else {
                raw
            }

        require(unsigned.size <= size) {
            "EC coordinate exceeds expected size."
        }

        return ByteArray(size).also { output ->
            unsigned.copyInto(
                destination = output,
                destinationOffset = size - unsigned.size,
            )
        }
    }

    private companion object {
        const val CURVE_NAME = "secp256r1"
        const val COORDINATE_SIZE = 32
    }
}
