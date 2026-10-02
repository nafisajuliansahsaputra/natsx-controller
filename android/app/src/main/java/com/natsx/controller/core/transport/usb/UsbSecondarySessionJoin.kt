package com.natsx.controller.core.transport.usb

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
 * Joins USB Direct to an already-active logical controller session.
 *
 * No long-term trust secret is used here. The secondary transport proves
 * possession of the current session key stored in TrustedSessionRegistry.
 */
class UsbSecondarySessionJoinClient(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val sessionRegistry: TrustedSessionRegistry,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos()
            .toULong() / 1_000uL
    },
) {
    fun joinUplinkOnly(
        outputStream: OutputStream,
    ): UsbTrustedSession {
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
                    UsbTrustedSession(
                        material.sessionId,
                        sessionKey,
                    )

                try {
                    writeFrame(
                        outputStream,
                        UsbControlFrameCodec
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

    fun join(
        inputStream: InputStream,
        outputStream: OutputStream,
    ): UsbTrustedSession {
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
                    UsbTrustedSession(
                        material.sessionId,
                        sessionKey,
                    )

                try {
                    writeFrame(
                        outputStream,
                        UsbControlFrameCodec
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
                        UsbControlFrameCodec
                            .decodeSessionReady(
                                UsbStreamFrameCodec
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
                            TransportCapabilities.USB_DIRECT == 0
                    ) {
                        throw GeneralSecurityException(
                            "USB secondary SESSION_READY does not match the active Windows receiver.",
                        )
                    }

                    writeFrame(
                        outputStream,
                        UsbControlFrameCodec
                            .encodeTransportReady(
                                trustedSession =
                                    trustedSession,
                                transport =
                                    ProtocolTransport.USB_DIRECT,
                                monotonicTimestampMicros =
                                    monotonicMicros(),
                            ),
                    )

                    val remoteTransportReady =
                        UsbControlFrameCodec
                            .decodeTransportReady(
                                UsbStreamFrameCodec
                                    .readFrame(
                                        inputStream,
                                    ),
                                trustedSession,
                            )

                    if (
                        remoteTransportReady.transport !=
                        ProtocolTransport.USB_DIRECT
                    ) {
                        throw GeneralSecurityException(
                            "USB secondary join received TRANSPORT_READY for a different transport.",
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
            UsbStreamFrameCodec.encode(
                frameBytes,
            ),
        )
        outputStream.flush()
    }
}
