package com.natsx.controller.core.protocol

import java.io.Closeable

class TrustedSessionMaterial(
    val sessionId: SessionId,
    sessionKey: ByteArray,
) : Closeable {
    private val key = sessionKey.copyOf()
    private var closed = false

    init {
        require(sessionId != SessionId.Zero) {
            "Trusted controller session requires a non-zero session ID."
        }

        require(
            key.size ==
                TrustedReconnectCrypto.SESSION_KEY_SIZE,
        ) {
            "Session key must be exactly " +
                TrustedReconnectCrypto.SESSION_KEY_SIZE +
                " bytes."
        }
    }

    fun copySessionKey(): ByteArray {
        check(!closed) {
            "Trusted session material is closed."
        }

        return key.copyOf()
    }

    fun copy(): TrustedSessionMaterial {
        check(!closed) {
            "Trusted session material is closed."
        }

        return TrustedSessionMaterial(
            sessionId,
            key,
        )
    }

    override fun close() {
        if (closed) return

        key.fill(0)
        closed = true
    }
}

data class TrustedSessionRegistration(
    val peerId: PeerId,
    val material: TrustedSessionMaterial,
) : Closeable {
    override fun close() {
        material.close()
    }
}

class TrustedSessionRegistry : Closeable {
    private val gate = Any()
    private val sessions =
        mutableMapOf<PeerId, TrustedSessionMaterial>()

    @Volatile
    private var closed = false

    fun replace(
        peerId: PeerId,
        sessionId: SessionId,
        sessionKey: ByteArray,
    ) {
        check(!closed) {
            "Trusted session registry is closed."
        }

        val replacement =
            TrustedSessionMaterial(
                sessionId,
                sessionKey,
            )

        synchronized(gate) {
            sessions
                .put(
                    peerId,
                    replacement,
                )
                ?.close()
        }
    }

    fun get(
        peerId: PeerId,
    ): TrustedSessionMaterial? {
        check(!closed) {
            "Trusted session registry is closed."
        }

        return synchronized(gate) {
            sessions[peerId]?.copy()
        }
    }

    fun getBySessionId(
        sessionId: SessionId,
    ): TrustedSessionRegistration? {
        check(!closed) {
            "Trusted session registry is closed."
        }

        if (sessionId == SessionId.Zero) {
            return null
        }

        return synchronized(gate) {
            sessions.entries
                .firstOrNull {
                    it.value.sessionId ==
                        sessionId
                }
                ?.let {
                    TrustedSessionRegistration(
                        peerId = it.key,
                        material = it.value.copy(),
                    )
                }
        }
    }

    fun remove(
        peerId: PeerId,
    ) {
        check(!closed) {
            "Trusted session registry is closed."
        }

        synchronized(gate) {
            sessions
                .remove(peerId)
                ?.close()
        }
    }

    fun clear() {
        check(!closed) {
            "Trusted session registry is closed."
        }

        synchronized(gate) {
            clearUnsafe()
        }
    }

    override fun close() {
        if (closed) return

        synchronized(gate) {
            clearUnsafe()
        }

        closed = true
    }

    private fun clearUnsafe() {
        sessions.values.forEach {
            it.close()
        }

        sessions.clear()
    }
}
