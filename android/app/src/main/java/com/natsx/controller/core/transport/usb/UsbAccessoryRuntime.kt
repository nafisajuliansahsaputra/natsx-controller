package com.natsx.controller.core.transport.usb

import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.SystemClock
import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.pairing.PairingPrompt
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.PairingAbortPayload
import com.natsx.controller.core.protocol.PairingAbortReason
import com.natsx.controller.core.protocol.PairingConfirmPayload
import com.natsx.controller.core.protocol.PairingFrameCodec
import com.natsx.controller.core.protocol.PairingInitiatorSession
import com.natsx.controller.core.protocol.ProtocolConstants
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.trust.TrustedPeerRecord
import com.natsx.controller.core.trust.TrustedPeerStore
import java.io.Closeable
import java.io.EOFException
import java.net.SocketTimeoutException
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class UsbAccessoryRuntime(
    private val usbManager: UsbManager,
    private val localPeerId: com.natsx.controller.core.protocol.PeerId,
    private val trustedPeerStore: TrustedPeerStore,
    private val sessionRegistry: TrustedSessionRegistry,
    private val broadcaster: RealtimeStateBroadcaster,
    private val pairingConfirmation: PairingConfirmationCoordinator,
    private val status: UsbRuntimeStatusCoordinator,
) : Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-usb-accessory-runtime").apply {
                isDaemon = true
            }
        }

    private val closed = AtomicBoolean(false)
    private val connectInFlight = AtomicBoolean(false)
    private val gate = Any()

    private var connection: UsbAccessoryConnection? = null
    private var trustedSession: UsbTrustedSession? = null
    private var sender: UsbRealtimeSender? = null
    private var activeAccessory: UsbAccessory? = null

    fun connect(accessory: UsbAccessory) {
        if (closed.get() || !UsbAccessoryIdentity.matches(accessory)) {
            return
        }

        synchronized(gate) {
            if (activeAccessory == accessory && sender != null) {
                return
            }
        }

        if (!connectInFlight.compareAndSet(false, true)) {
            status.publish(
                "USB connection attempt already in progress.",
            )
            return
        }

        status.publish(
            "USB accessory detected. Starting session…",
        )

        executor.execute {
            try {
                connectBlocking(accessory)
            } catch (exception: Exception) {
                val detail =
                    exception.message
                        ?.takeIf { it.isNotBlank() }
                        ?: exception::class.java.simpleName

                status.publish(
                    "USB error: $detail",
                    isError = true,
                )
            } finally {
                connectInFlight.set(false)
            }
        }
    }

    fun disconnect(accessory: UsbAccessory? = null) {
        if (closed.get()) {
            return
        }

        executor.execute {
            disconnectBlocking(accessory)
        }
    }

    fun isConnected(): Boolean =
        synchronized(gate) {
            sender != null
        }

    private fun connectBlocking(accessory: UsbAccessory) {
        synchronized(gate) {
            if (activeAccessory == accessory && sender != null) {
                return
            }
        }

        disconnectBlocking(null)

        if (!usbManager.hasPermission(accessory)) {
            status.publish(
                "USB permission is not granted.",
                isError = true,
            )
            return
        }

        status.publish(
            "Opening USB accessory…",
        )

        val opened =
            UsbAccessoryConnection.open(
                usbManager,
                accessory,
            )

        status.publish(
            "USB accessory opened.",
        )

        var session: UsbTrustedSession? = null
        var realtimeSender: UsbRealtimeSender? = null

        try {
            val peer =
                resolveReceiverPeer()
                    ?: if (
                        trustedPeerStore
                            .list()
                            .isEmpty()
                    ) {
                        status.publish(
                            "No trusted PC yet. Pair over LAN first; USB will attach automatically after trust is established.",
                        )

                        opened.close()
                        return
                    } else {
                        opened.close()
                        return
                    }

            status.publish(
                "Starting trusted USB authentication…",
            )

            session =
                connectTrustedSession(
                    opened,
                    peer,
                )

            realtimeSender =
                UsbRealtimeSender(
                    outputStream = opened.output,
                    trustedSession = session,
                    inputStream = opened.input,
                )

            broadcaster.addSink(realtimeSender)

            synchronized(gate) {
                connection = opened
                trustedSession = session
                sender = realtimeSender
                activeAccessory = accessory
            }

            status.publish(
                "USB authenticated. Realtime controller active.",
            )
        } catch (exception: Exception) {
            realtimeSender?.let(broadcaster::removeSink)
            realtimeSender?.close()
            session?.close()
            opened.close()
            throw exception
        }
    }

    private fun performFirstPairing(
        opened: UsbAccessoryConnection,
    ): TrustedPeerRecord {
        PairingInitiatorSession(
            localPeerId,
        ).use { pairing ->
            status.publish(
                "Sending pairing offer to Windows…",
            )

            writePairingFrame(
                opened,
                PairingFrameCodec
                    .encodeOffer(
                        pairing.offer,
                    ),
            )

            status.publish(
                "Pairing offer sent. Waiting for Windows response…",
            )

            val responseFrame =
                readFrameWithTimeout(
                    opened = opened,
                    timeoutMillis = 20_000,
                    stage =
                        "Waiting for Windows pairing response",
                )

            when (readMessageType(responseFrame)) {
                MessageType.PAIRING_ABORT -> {
                    val abort =
                        PairingFrameCodec
                            .decodeAbort(
                                responseFrame,
                            )

                    error(
                        "Windows rejected pairing: " +
                            abort.reason,
                    )
                }

                MessageType.PAIRING_RESPONSE -> Unit

                else -> error(
                    "Expected PAIRING_RESPONSE from Windows.",
                )
            }

            status.publish(
                "Windows pairing response received.",
            )

            val response =
                PairingFrameCodec
                    .decodeResponse(
                        responseFrame,
                    )

            val code =
                pairing.acceptResponse(
                    response,
                )

            status.publish(
                "Pairing code ready: $code",
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
                status.publish(
                    "Pairing rejected or timed out on Android.",
                    isError = true,
                )
                sendPairingAbort(
                    opened,
                    PairingAbortReason.USER_REJECTED,
                )

                error(
                    "Pairing was rejected or timed out on Android.",
                )
            }

            val localConfirm =
                pairing
                    .approveDisplayedCode()

            status.publish(
                "Android confirmed code. Waiting for Windows confirmation…",
            )

            writePairingFrame(
                opened,
                PairingFrameCodec
                    .encodeConfirm(
                        localConfirm,
                    ),
            )

            val remoteFrame =
                readFrameWithTimeout(
                    opened = opened,
                    timeoutMillis = 120_000,
                    stage =
                        "Waiting for Windows pairing confirmation",
                )

            status.publish(
                "Windows pairing confirmation frame received.",
            )

            val remoteConfirm:
                PairingConfirmPayload =
                when (
                    readMessageType(
                        remoteFrame,
                    )
                ) {
                    MessageType.PAIRING_CONFIRM ->
                        PairingFrameCodec
                            .decodeConfirm(
                                remoteFrame,
                            )

                    MessageType.PAIRING_ABORT -> {
                        val abort =
                            PairingFrameCodec
                                .decodeAbort(
                                    remoteFrame,
                                )

                        error(
                            "Windows aborted pairing: " +
                                abort.reason,
                        )
                    }

                    else ->
                        error(
                            "Expected PAIRING_CONFIRM from Windows.",
                        )
                }

            status.publish(
                "Verifying Windows pairing confirmation…",
            )

            pairing
                .acceptRemoteConfirmation(
                    remoteConfirm,
                ).use { established ->
                    status.publish(
                        "Windows pairing confirmation verified.",
                    )
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

                        status.publish(
                            "Saving trusted Windows receiver…",
                        )

                        trustedPeerStore.put(
                            record,
                            secret,
                        )

                        status.publish(
                            "Pairing complete. Windows receiver trusted.",
                        )

                        return record
                    } finally {
                        secret.fill(0)
                    }
                }
        }
    }

    private fun sendPairingAbort(
        opened: UsbAccessoryConnection,
        reason: PairingAbortReason,
    ) {
        runCatching {
            writePairingFrame(
                opened,
                PairingFrameCodec
                    .encodeAbort(
                        PairingAbortPayload(
                            reason,
                        ),
                    ),
            )
        }
    }

    private fun writePairingFrame(
        opened: UsbAccessoryConnection,
        frame: ByteArray,
    ) {
        val packet =
            UsbStreamFrameCodec
                .encode(frame)

        try {
            opened.output.write(
                packet,
            )
            opened.output.flush()
        } finally {
            frame.fill(0)
            packet.fill(0)
        }
    }

    private fun readFrameWithTimeout(
        opened: UsbAccessoryConnection,
        timeoutMillis: Long,
        stage: String,
    ): ByteArray {
        require(timeoutMillis > 0)

        val deadline =
            SystemClock.elapsedRealtime() +
                timeoutMillis

        val prefix =
            ByteArray(
                UsbStreamFrameCodec
                    .LENGTH_PREFIX_SIZE,
            )

        readExactlyWithTimeout(
            opened = opened,
            destination = prefix,
            deadlineMillis = deadline,
            stage = stage,
        )

        val frame =
            ByteArray(
                UsbStreamFrameCodec
                    .decodeLengthPrefix(
                        prefix,
                    ),
            )

        readExactlyWithTimeout(
            opened = opened,
            destination = frame,
            deadlineMillis = deadline,
            stage = stage,
        )

        return frame
    }

    private fun readExactlyWithTimeout(
        opened: UsbAccessoryConnection,
        destination: ByteArray,
        deadlineMillis: Long,
        stage: String,
    ) {
        var offset = 0

        while (offset < destination.size) {
            val remaining =
                deadlineMillis -
                    SystemClock.elapsedRealtime()

            if (remaining <= 0) {
                throw SocketTimeoutException(
                    "$stage timed out.",
                )
            }

            opened.awaitReadable(
                timeoutMillis =
                    remaining
                        .coerceAtMost(
                            Int.MAX_VALUE.toLong(),
                        )
                        .toInt(),
                stage = stage,
            )

            val read =
                opened.input.read(
                    destination,
                    offset,
                    destination.size - offset,
                )

            if (read < 0) {
                throw EOFException(
                    "$stage ended before a complete frame was received.",
                )
            }

            if (read == 0) {
                continue
            }

            offset += read
        }
    }

    private fun readMessageType(
        frame: ByteArray,
    ): MessageType {
        require(
            frame.size >=
                ProtocolConstants.HEADER_SIZE,
        ) {
            "USB pairing frame is shorter than the protocol header."
        }

        return requireNotNull(
            MessageType.fromWireValue(
                frame[6].toInt() and 0xFF,
            ),
        ) {
            "USB pairing frame has an unknown message type."
        }
    }

    private fun connectTrustedSession(
        opened: UsbAccessoryConnection,
        peer: TrustedPeerRecord,
    ): UsbTrustedSession {
        val active =
            sessionRegistry.get(peer.peerId)

        if (active != null) {
            active.close()

            return UsbSecondarySessionJoinClient(
                localPeerId = localPeerId,
                receiverPeerId = peer.peerId,
                sessionRegistry = sessionRegistry,
            ).joinUplinkOnly(
                outputStream = opened.output,
            )
        }

        val material =
            checkNotNull(
                trustedPeerStore.get(peer.peerId),
            ) {
                "Trusted Windows receiver material is unavailable."
            }

        material.use {
            val secret =
                material.copyTrustSecret()

            try {
                return UsbTrustedReconnectClient(
                    localPeerId = localPeerId,
                    sessionRegistry = sessionRegistry,
                ).connect(
                    inputStream = opened.input,
                    outputStream = opened.output,
                    receiverPeerId = peer.peerId,
                    trustSecret = secret,
                )
            } finally {
                secret.fill(0)
            }
        }
    }

    private fun resolveReceiverPeer(): TrustedPeerRecord? {
        val usbPeers =
            trustedPeerStore.list()
                .filter {
                    it.capabilities and
                        TransportCapabilities.USB_DIRECT != 0
                }
                .ifEmpty {
                    trustedPeerStore.list()
                }

        val activePeers =
            usbPeers.filter { peer ->
                sessionRegistry.get(peer.peerId)
                    ?.let {
                        it.close()
                        true
                    }
                    ?: false
            }

        if (activePeers.size == 1) {
            return activePeers.single()
        }

        if (activePeers.isEmpty() && usbPeers.size == 1) {
            return usbPeers.single()
        }

        // Never guess between multiple trusted PCs. M10 pairing/UX will expose
        // explicit receiver selection for that case.
        return null
    }

    private fun disconnectBlocking(
        accessory: UsbAccessory?,
    ) {
        val oldSender: UsbRealtimeSender?
        val oldSession: UsbTrustedSession?
        val oldConnection: UsbAccessoryConnection?

        synchronized(gate) {
            if (
                accessory != null &&
                activeAccessory != null &&
                activeAccessory != accessory
            ) {
                return
            }

            oldSender = sender
            oldSession = trustedSession
            oldConnection = connection

            sender = null
            trustedSession = null
            connection = null
            activeAccessory = null
        }

        if (oldSender != null) {
            broadcaster.removeSink(oldSender)
            oldSender.close()
        }

        oldSession?.close()
        oldConnection?.close()

        if (accessory != null) {
            status.publish(
                "USB accessory disconnected.",
            )
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        // Run cleanup synchronously after preventing new work.
        disconnectBlocking(null)
        executor.shutdownNow()
    }
}
