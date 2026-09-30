package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.SessionId
import java.io.Closeable

class WifiTrustedSession(
    val sessionId: SessionId,
    sessionKey: ByteArray,
) : Closeable {
    private val key = sessionKey.copyOf()
    private var closed = false

    init {
        require(key.size == SESSION_KEY_SIZE) {
            "Wi-Fi session key must be exactly $SESSION_KEY_SIZE bytes."
        }
    }

    internal fun authenticationKey(): ByteArray {
        check(!closed) { "Trusted Wi-Fi session is closed." }
        return key
    }

    override fun close() {
        if (closed) return
        key.fill(0)
        closed = true
    }

    companion object {
        const val SESSION_KEY_SIZE = 32
    }
}
