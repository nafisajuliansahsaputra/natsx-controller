package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.pairing.PairingPrompt
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PairingAbortReason
import com.natsx.controller.core.protocol.PairingFrameCodec
import com.natsx.controller.core.protocol.PairingInitiatorSession
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.trust.TrustedPeerRecord
import com.natsx.controller.core.trust.TrustedPeerStore
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException

class WifiFirstPairingClient(
    private val localPeerId: PeerId,
    private val trustedPeerStore: TrustedPeerStore,
    private val pairingConfirmation: PairingConfirmationCoordinator,
    private val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
) {
    init {
        require(timeoutMillis in 500..10_000)
    }

    fun pair(): TrustedPeerRecord {
        PairingInitiatorSession(localPeerId).use { pairing ->
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = timeoutMillis

                val offer =
                    PairingFrameCodec.encodeOffer(
                        pairing.offer,
                    )

                val broadcast =
                    InetSocketAddress(
                        InetAddress.getByName(
                            BROADCAST_ADDRESS,
                        ),
                        PAIRING_PORT,
                    )

                socket.send(
                    DatagramPacket(
                        offer,
                        offer.size,
                        broadcast,
                    ),
                )

                val responsePacket =
                    receive(
                        socket,
                        "Windows LAN pairing response",
                    )

                val responseBytes =
                    responsePacket.data.copyOfRange(
                        responsePacket.offset,
                        responsePacket.offset +
                            responsePacket.length,
                    )

                when (
                    ProtocolFrameCodec
                        .decode(responseBytes)
                        .messageType
                ) {
                    MessageType.PAIRING_ABORT -> {
                        val abort =
                            PairingFrameCodec
                                .decodeAbort(
                                    responseBytes,
                                )

                        error(
                            "Windows rejected LAN pairing: " +
                                abort.reason,
                        )
                    }

                    MessageType.PAIRING_RESPONSE -> Unit

                    else ->
                        error(
                            "Expected PAIRING_RESPONSE from Windows over LAN.",
                        )
                }

                val response =
                    PairingFrameCodec
                        .decodeResponse(
                            responseBytes,
                        )

                val code =
                    pairing.acceptResponse(
                        response,
                    )

                val approved =
                    pairingConfirmation
                        .requestConfirmation(
                            PairingPrompt(
                                comparisonCode = code,
                                remotePeerId =
                                    response.windowsPeerId,
                            ),
                        )

                if (!approved) {
                    error(
                        "LAN pairing was rejected or timed out on Android.",
                    )
                }

                val windowsEndpoint =
                    InetSocketAddress(
                        responsePacket.address,
                        responsePacket.port,
                    )

                val localConfirm =
                    PairingFrameCodec
                        .encodeConfirm(
                            pairing
                                .approveDisplayedCode(),
                        )

                socket.send(
                    DatagramPacket(
                        localConfirm,
                        localConfirm.size,
                        windowsEndpoint,
                    ),
                )

                val remotePacket =
                    receive(
                        socket,
                        "Windows LAN pairing confirmation",
                    )

                if (
                    remotePacket.address !=
                        responsePacket.address
                ) {
                    throw IOException(
                        "LAN pairing confirmation came from a different host.",
                    )
                }

                val remoteBytes =
                    remotePacket.data.copyOfRange(
                        remotePacket.offset,
                        remotePacket.offset +
                            remotePacket.length,
                    )

                when (
                    ProtocolFrameCodec
                        .decode(remoteBytes)
                        .messageType
                ) {
                    MessageType.PAIRING_CONFIRM -> Unit

                    MessageType.PAIRING_ABORT -> {
                        val abort =
                            PairingFrameCodec
                                .decodeAbort(
                                    remoteBytes,
                                )

                        error(
                            "Windows aborted LAN pairing: " +
                                abort.reason,
                        )
                    }

                    else ->
                        error(
                            "Expected PAIRING_CONFIRM from Windows over LAN.",
                        )
                }

                pairing
                    .acceptRemoteConfirmation(
                        PairingFrameCodec
                            .decodeConfirm(
                                remoteBytes,
                            ),
                    ).use { established ->
                        val secret =
                            established
                                .copyTrustSecret()

                        try {
                            val record =
                                TrustedPeerRecord(
                                    peerId =
                                        established
                                            .remotePeerId,
                                    displayName =
                                        "NATSX Windows Receiver",
                                    capabilities =
                                        TransportCapabilities.WIFI or
                                            TransportCapabilities.BLUETOOTH or
                                            TransportCapabilities.USB_DIRECT,
                                    pairedAtEpochMillis =
                                        System.currentTimeMillis(),
                                )

                            trustedPeerStore.put(
                                record,
                                secret,
                            )

                            return record
                        } finally {
                            secret.fill(0)
                        }
                    }
            }
        }
    }

    private fun receive(
        socket: DatagramSocket,
        stage: String,
    ): DatagramPacket {
        val buffer =
            ByteArray(
                MAXIMUM_DATAGRAM_SIZE,
            )

        val packet =
            DatagramPacket(
                buffer,
                buffer.size,
            )

        try {
            socket.receive(packet)
        } catch (exception: SocketTimeoutException) {
            throw IOException(
                "$stage timed out. Make sure the PC and phone are on the same LAN and Windows Firewall allows NATSX Controller.",
                exception,
            )
        }

        return packet
    }

    companion object {
        const val PAIRING_PORT = 43858
        const val DEFAULT_TIMEOUT_MILLIS = 5_000
        const val MAXIMUM_DATAGRAM_SIZE = 512
        const val BROADCAST_ADDRESS = "255.255.255.255"
    }
}
