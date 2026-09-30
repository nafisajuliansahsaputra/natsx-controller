package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.trust.TrustedPeerStore
import java.net.InetSocketAddress

class TrustedWifiRealtimeLinkFactory(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val trustedPeerStore: TrustedPeerStore,
    private val reconnectClient: WifiTrustedReconnectClient =
        WifiTrustedReconnectClient(localPeerId),
    private val sessionRegistry: TrustedSessionRegistry? = null,
) : WifiRealtimeLinkFactory {
    override fun create(endpoint: InetSocketAddress): WifiRealtimeLink {
        val material =
            checkNotNull(trustedPeerStore.get(receiverPeerId)) {
                "Receiver is not trusted. First pairing is required."
            }

        material.use {
            val trustSecret = material.copyTrustSecret()

            try {
                val session = reconnectClient.connect(
                    controlEndpoint =
                        WifiTrustedReconnectClient.controlEndpointForRealtime(
                            endpoint,
                        ),
                    receiverPeerId = receiverPeerId,
                    trustSecret = trustSecret,
                )

                try {
                    sessionRegistry?.replace(
                        peerId = receiverPeerId,
                        sessionId = session.sessionId,
                        sessionKey = session.authenticationKey(),
                    )

                    return OwnedWifiRealtimeLink(
                        delegate = WifiRealtimeSender(
                            remoteEndpoint = endpoint,
                            trustedSession = session,
                        ),
                        ownedSession = session,
                    )
                } catch (exception: Exception) {
                    session.close()
                    throw exception
                }
            } finally {
                trustSecret.fill(0)
            }
        }
    }
}

private class OwnedWifiRealtimeLink(
    private val delegate: WifiRealtimeLink,
    private val ownedSession: WifiTrustedSession,
) : WifiRealtimeLink {
    private var closed = false

    override val lastHeartbeatReceivedNanos: Long
        get() = delegate.lastHeartbeatReceivedNanos

    override fun publish(envelope: RealtimeStateEnvelope) {
        check(!closed) {
            "Owned Wi-Fi realtime link is closed."
        }
        delegate.publish(envelope)
    }

    override fun close() {
        if (closed) return
        closed = true

        try {
            delegate.close()
        } finally {
            ownedSession.close()
        }
    }
}
