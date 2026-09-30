package com.natsx.controller.core.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.trust.TrustedPeerStore
import java.io.IOException

class TrustedBluetoothRealtimeLinkFactory(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val trustedPeerStore: TrustedPeerStore,
    private val sessionRegistry: TrustedSessionRegistry,
    private val candidateProvider:
        BluetoothRfcommCandidateProvider,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos()
            .toULong() / 1_000uL
    },
) : BluetoothRealtimeLinkFactory {
    override fun create():
        BluetoothRealtimeLink {
        val candidates =
            candidateProvider.candidates()

        if (candidates.isEmpty()) {
            throw IOException(
                "No bonded Bluetooth RFCOMM candidates are available.",
            )
        }

        var lastFailure: Throwable? = null

        candidates.forEach { candidate ->
            val connection =
                try {
                    candidate.connect()
                } catch (exception: Exception) {
                    lastFailure = exception
                    return@forEach
                }

            try {
                val session =
                    openSession(connection)

                try {
                    val sender =
                        BluetoothRealtimeSender(
                            outputStream =
                                connection.outputStream,
                            trustedSession =
                                session,
                            inputStream =
                                connection.inputStream,
                        )

                    return OwnedBluetoothRealtimeLink(
                        delegate = sender,
                        connection = connection,
                        session = session,
                    )
                } catch (exception: Exception) {
                    session.close()
                    throw exception
                }
            } catch (exception: Exception) {
                lastFailure = exception
                runCatching {
                    connection.close()
                }
            }
        }

        throw IOException(
            "Unable to authenticate any bonded NATSX Bluetooth candidate.",
            lastFailure,
        )
    }

    private fun openSession(
        connection: BluetoothRfcommConnection,
    ): BluetoothTrustedSession {
        val activeSession =
            sessionRegistry.get(
                receiverPeerId,
            )

        if (activeSession != null) {
            activeSession.close()

            return BluetoothSecondarySessionJoinClient(
                localPeerId = localPeerId,
                receiverPeerId = receiverPeerId,
                sessionRegistry = sessionRegistry,
                monotonicMicros = monotonicMicros,
            ).join(
                inputStream =
                    connection.inputStream,
                outputStream =
                    connection.outputStream,
            )
        }

        val material =
            checkNotNull(
                trustedPeerStore.get(
                    receiverPeerId,
                ),
            ) {
                "Receiver is not trusted. First pairing is required."
            }

        material.use {
            val trustSecret =
                material.copyTrustSecret()

            try {
                return BluetoothTrustedReconnectClient(
                    localPeerId = localPeerId,
                    sessionRegistry =
                        sessionRegistry,
                    monotonicMicros =
                        monotonicMicros,
                ).connect(
                    inputStream =
                        connection.inputStream,
                    outputStream =
                        connection.outputStream,
                    receiverPeerId =
                        receiverPeerId,
                    trustSecret =
                        trustSecret,
                )
            } finally {
                trustSecret.fill(0)
            }
        }
    }
}

private class OwnedBluetoothRealtimeLink(
    private val delegate:
        BluetoothRealtimeSender,
    private val connection:
        BluetoothRfcommConnection,
    private val session:
        BluetoothTrustedSession,
) : BluetoothRealtimeLink {
    private var closed = false

    override val lastHeartbeatReceivedNanos:
        Long
        get() =
            delegate.lastHeartbeatReceivedNanos

    override fun publish(
        envelope: RealtimeStateEnvelope,
    ) {
        check(!closed) {
            "Owned Bluetooth realtime link is closed."
        }

        delegate.publish(envelope)
    }

    override fun close() {
        if (closed) return
        closed = true

        try {
            delegate.close()
        } finally {
            try {
                session.close()
            } finally {
                runCatching {
                    connection.close()
                }
            }
        }
    }
}
