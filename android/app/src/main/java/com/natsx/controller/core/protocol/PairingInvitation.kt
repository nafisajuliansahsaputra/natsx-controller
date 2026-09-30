package com.natsx.controller.core.protocol

import java.io.Closeable
import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.security.SecureRandom
import java.util.Base64

class PairingInvitation(
    val receiverPeerId: PeerId,
    secret: ByteArray,
    val expiresUnixSeconds: Long,
) : Closeable {
    private val secretBytes = secret.copyOf()
    private var closed = false

    init {
        require(secretBytes.size == PairingCrypto.SECRET_SIZE) {
            "Pairing invitation secret must be exactly ${PairingCrypto.SECRET_SIZE} bytes."
        }
    }

    fun copySecret(): ByteArray {
        check(!closed) { "Pairing invitation is closed." }
        return secretBytes.copyOf()
    }

    fun isExpired(nowUnixSeconds: Long): Boolean =
        nowUnixSeconds > expiresUnixSeconds

    fun toUriString(): String {
        check(!closed) { "Pairing invitation is closed." }

        val encodedSecret = Base64
            .getUrlEncoder()
            .withoutPadding()
            .encodeToString(secretBytes)

        return "natsx://pair/v1" +
            "?peer=$receiverPeerId" +
            "&secret=$encodedSecret" +
            "&expires=$expiresUnixSeconds"
    }

    override fun toString(): String =
        "PairingInvitation(" +
            "ReceiverPeerId=$receiverPeerId, " +
            "ExpiresUnixSeconds=$expiresUnixSeconds, " +
            "Secret=[redacted])"

    override fun close() {
        if (closed) return
        secretBytes.fill(0)
        closed = true
    }

    companion object {
        fun create(
            receiverPeerId: PeerId,
            expiresUnixSeconds: Long,
        ): PairingInvitation {
            val secret = ByteArray(PairingCrypto.SECRET_SIZE)
            SecureRandom().nextBytes(secret)

            return try {
                PairingInvitation(
                    receiverPeerId,
                    secret,
                    expiresUnixSeconds,
                )
            } finally {
                secret.fill(0)
            }
        }

        fun parse(value: String): PairingInvitation {
            require(value.isNotBlank()) {
                "Pairing invitation must not be blank."
            }

            val uri = runCatching { URI(value) }
                .getOrElse {
                    throw IllegalArgumentException(
                        "Invalid NATSX pairing invitation URI.",
                        it,
                    )
                }

            require(
                uri.scheme.equals("natsx", ignoreCase = true) &&
                    uri.host.equals("pair", ignoreCase = true) &&
                    uri.path == "/v1"
            ) {
                "Invalid NATSX pairing invitation URI."
            }

            val query = parseQuery(uri.rawQuery ?: "")
            val peerValue = requireNotNull(query["peer"]) {
                "Pairing invitation is missing peer."
            }
            val secretValue = requireNotNull(query["secret"]) {
                "Pairing invitation is missing secret."
            }
            val expiresValue = requireNotNull(query["expires"]) {
                "Pairing invitation is missing expiry."
            }

            val peerBytes = decodeHex(peerValue)
            val secret = runCatching {
                Base64.getUrlDecoder().decode(secretValue)
            }.getOrElse {
                throw IllegalArgumentException(
                    "Pairing invitation secret is invalid.",
                    it,
                )
            }

            return try {
                PairingInvitation(
                    receiverPeerId = PeerId.fromBytes(peerBytes),
                    secret = secret,
                    expiresUnixSeconds = expiresValue.toLongOrNull()
                        ?: throw IllegalArgumentException(
                            "Pairing invitation expiry is invalid.",
                        ),
                )
            } finally {
                secret.fill(0)
            }
        }

        private fun parseQuery(rawQuery: String): Map<String, String> {
            if (rawQuery.isBlank()) return emptyMap()

            val output = linkedMapOf<String, String>()

            rawQuery.split("&").forEach { pair ->
                val pieces = pair.split("=", limit = 2)
                require(pieces.size == 2) {
                    "Pairing invitation query is malformed."
                }

                val key = URLDecoder.decode(
                    pieces[0],
                    StandardCharsets.UTF_8,
                )
                val value = URLDecoder.decode(
                    pieces[1],
                    StandardCharsets.UTF_8,
                )

                require(output.put(key, value) == null) {
                    "Duplicate pairing invitation field: $key."
                }
            }

            return output
        }

        private fun decodeHex(value: String): ByteArray {
            require(value.length % 2 == 0) {
                "Peer ID hex is malformed."
            }

            return ByteArray(value.length / 2) { index ->
                val offset = index * 2
                value.substring(offset, offset + 2)
                    .toIntOrNull(16)
                    ?.toByte()
                    ?: throw IllegalArgumentException(
                        "Peer ID hex is malformed.",
                    )
            }
        }
    }
}
