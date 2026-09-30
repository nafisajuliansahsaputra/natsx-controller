package com.natsx.controller.core.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.PeerRole
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.SessionReadyPayload
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import java.io.InputStream
import java.io.OutputStream
import java.security.GeneralSecurityException

/**
 * Joins Bluetooth to an already-active logical controller session.
 *
 * No long-term trust secret is used here. The secondary transport proves
 * possession of the current session key stored in TrustedSessionRegistry.
 */
class BluetoothSecondarySessionJoinClient(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val sessionRegistry: TrustedSessionRegistry,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos()
            .toULong() / 1_000uL
    },
) {
    fun join(
        inputStream: InputStream,
        outputStream: OutputStream,
    ): BluetoothTrustedSession {
        val material =
            checkNotNull(
                sessionRegistry.get(
                    receiverPeerId,
                ),
            ) {
                "No active trusted controller session exists for the receiver."
            }

        material.use {
            val sessionKey =
                material.copySessionKey()

            try {
                val trustedSession =
                    BluetoothTrustedSession(
                        material.sessionId,
                        sessionKey,
                    )

                try {
                    writeFrame(
                        outputStream,
                        BluetoothControlFrameCodec
                            .encodeSessionReady(
                                trustedSession,
                                SessionReadyPayload(
                                    role =
                                        PeerRole.ANDROID_CONTROLLER,
                                    capabilities =
                                        TransportCapabilities.WIFI or
                                            TransportCapabilities.BLUETOOTH or
                                            TransportCapabilities.USB_DIRECT,
                                    peerId = localPeerId,
                                ),
                                monotonicMicros(),
                            ),
                    )

                    val remoteReady =
                        BluetoothControlFrameCodec
                            .decodeSessionReady(
                                BluetoothStreamFrameCodec
                                    .readFrame(
                                        inputStream,
                                    ),
                                trustedSession,
                            )

                    if (
                        remoteReady.role !=
                        PeerRole.WINDOWS_RECEIVER ||
                        remoteReady.peerId !=
                        receiverPeerId ||
                        remoteReady.capabilities and
                            TransportCapabilities.BLUETOOTH == 0
                    ) {
                        throw GeneralSecurityException(
                            "Bluetooth secondary SESSION_READY does not match the active Windows receiver.",
                        )
                    }

                    writeFrame(
                        outputStream,
                        BluetoothControlFrameCodec
                            .encodeTransportReady(
                                trustedSession =
                                    trustedSession,
                                transport =
                                    ProtocolTransport.BLUETOOTH,
                                monotonicTimestampMicros =
                                    monotonicMicros(),
                            ),
                    )

                    val remoteTransportReady =
                        BluetoothControlFrameCodec
                            .decodeTransportReady(
                                BluetoothStreamFrameCodec
                                    .readFrame(
                                        inputStream,
                                    ),
                                trustedSession,
                            )

                    if (
                        remoteTransportReady.transport !=
                        ProtocolTransport.BLUETOOTH
                    ) {
                        throw GeneralSecurityException(
                            "Bluetooth secondary join received TRANSPORT_READY for a different transport.",
                        )
                    }

                    return trustedSession
                } catch (exception: Exception) {
                    trustedSession.close()
                    throw exception
                }
            } finally {
                sessionKey.fill(0)
            }
        }
    }

    private fun writeFrame(
        outputStream: OutputStream,
        frameBytes: ByteArray,
    ) {
        outputStream.write(
            BluetoothStreamFrameCodec.encode(
                frameBytes,
            ),
        )
        outputStream.flush()
    }
}
