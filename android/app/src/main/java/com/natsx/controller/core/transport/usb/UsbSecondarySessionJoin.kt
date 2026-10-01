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

class UsbSecondarySessionJoinClient(
    private val localPeerId: PeerId,
    private val receiverPeerId: PeerId,
    private val sessionRegistry: TrustedSessionRegistry,
    private val monotonicMicros: () -> ULong = {
        SystemClock.elapsedRealtimeNanos().toULong() / 1_000uL
    },
) {
    fun join(
        inputStream: InputStream,
        outputStream: OutputStream,
    ): UsbTrustedSession {
        val material =
            checkNotNull(sessionRegistry.get(receiverPeerId)) {
                "No active trusted controller session exists for the receiver."
            }

        material.use {
            val sessionKey = material.copySessionKey()

            try {
                val session =
                    UsbTrustedSession(
                        material.sessionId,
                        sessionKey,
                    )

                try {
                    writeFrame(
                        outputStream,
                        UsbControlFrameCodec.encodeSessionReady(
                            session,
                            SessionReadyPayload(
                                role = PeerRole.ANDROID_CONTROLLER,
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
                        UsbControlFrameCodec.decodeSessionReady(
                            UsbStreamFrameCodec.readFrame(inputStream),
                            session,
                        )

                    if (
                        remoteReady.role != PeerRole.WINDOWS_RECEIVER ||
                        remoteReady.peerId != receiverPeerId ||
                        remoteReady.capabilities and
                            TransportCapabilities.USB_DIRECT == 0
                    ) {
                        throw GeneralSecurityException(
                            "USB secondary SESSION_READY does not match the active Windows receiver.",
                        )
                    }

                    writeFrame(
                        outputStream,
                        UsbControlFrameCodec.encodeTransportReady(
                            session,
                            monotonicMicros(),
                        ),
                    )

                    val transportReady =
                        UsbControlFrameCodec.decodeTransportReady(
                            UsbStreamFrameCodec.readFrame(inputStream),
                            session,
                        )

                    if (
                        transportReady.transport !=
                        ProtocolTransport.USB_DIRECT
                    ) {
                        throw GeneralSecurityException(
                            "USB secondary join received TRANSPORT_READY for a different transport.",
                        )
                    }

                    return session
                } catch (exception: Exception) {
                    session.close()
                    throw exception
                }
            } finally {
                sessionKey.fill(0)
            }
        }
    }

    private fun writeFrame(
        outputStream: OutputStream,
        frame: ByteArray,
    ) {
        outputStream.write(
            UsbStreamFrameCodec.encode(frame),
        )
        outputStream.flush()
    }
}
