package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.protocol.SessionId
import java.io.Closeable

class BluetoothTrustedSession(
    val sessionId: SessionId,
    sessionKey: ByteArray,
) : Closeable {
    private val key = sessionKey.copyOf()
    private var closed = false

    init {
        require(sessionId != SessionId.Zero) {
            "Bluetooth trusted session requires a non-zero session ID."
        }

        require(key.size == SESSION_KEY_SIZE) {
            "Bluetooth session key must be exactly $SESSION_KEY_SIZE bytes."
        }
    }

    internal fun authenticationKey(): ByteArray {
        check(!closed) {
            "Trusted Bluetooth session is closed."
        }

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
