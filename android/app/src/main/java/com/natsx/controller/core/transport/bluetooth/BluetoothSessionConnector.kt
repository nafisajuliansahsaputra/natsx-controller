package com.natsx.controller.core.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.trust.TrustedPeerStore
import java.io.InputStream
import java.io.OutputStream

/** Join a warm session, or authenticate directly from saved trust when Wi-Fi is unavailable. */
class BluetoothSessionConnector(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val registry: TrustedSessionRegistry,
    private val trustStore: TrustedPeerStore?,
    private val monotonicMicros: () -> ULong = { SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL },
) {
    private var ownedSessionId: SessionId? = null

    fun connect(input: InputStream, output: OutputStream): BluetoothTrustedSession {
        val existing = registry.get(receiverPeerId)
        if (existing != null) {
            existing.close()
            return BluetoothSecondarySessionJoinClient(localPeerId, receiverPeerId, registry, monotonicMicros)
                .join(input, output)
        }
        return checkNotNull(trustStore?.get(receiverPeerId)) {
            "Receiver must be paired before trusted Bluetooth reconnect."
        }.use { material ->
            val secret = material.copyTrustSecret()
            try {
                BluetoothTrustedReconnectClient(localPeerId, registry, monotonicMicros)
                    .connect(input, output, receiverPeerId, secret)
                    .also { ownedSessionId = it.sessionId }
            } finally { secret.fill(0) }
        }
    }

    fun sessionClosed() {
        ownedSessionId?.let { registry.removeIfSession(receiverPeerId, it) }
        ownedSessionId = null
    }
}
