package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedSessionRegistry

internal object UsbSessionRecovery {
    fun isCurrent(
        registry: TrustedSessionRegistry,
        peerId: PeerId,
        sessionId: SessionId,
    ): Boolean {
        // Losing the wireless registration alone must not stop working USB.
        val current = registry.get(peerId) ?: return true
        return current.use { it.sessionId == sessionId }
    }
}
