package com.natsx.controller.core.transport.usb

import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import com.natsx.controller.core.protocol.TransportCapabilities
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.trust.TrustedPeerRecord
import com.natsx.controller.core.trust.TrustedPeerStore
import java.io.Closeable
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class UsbAccessoryRuntime(
    private val usbManager: UsbManager,
    private val localPeerId: com.natsx.controller.core.protocol.PeerId,
    private val trustedPeerStore: TrustedPeerStore,
    private val sessionRegistry: TrustedSessionRegistry,
    private val broadcaster: RealtimeStateBroadcaster,
) : Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-usb-accessory-runtime").apply {
                isDaemon = true
            }
        }

    private val closed = AtomicBoolean(false)
    private val gate = Any()

    private var connection: UsbAccessoryConnection? = null
    private var trustedSession: UsbTrustedSession? = null
    private var sender: UsbRealtimeSender? = null
    private var activeAccessory: UsbAccessory? = null

    fun connect(accessory: UsbAccessory) {
        if (closed.get() || !UsbAccessoryIdentity.matches(accessory)) {
            return
        }

        executor.execute {
            runCatching {
                connectBlocking(accessory)
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
            return
        }

        val peer = resolveReceiverPeer()
            ?: return

        val opened =
            UsbAccessoryConnection.open(
                usbManager,
                accessory,
            )

        var session: UsbTrustedSession? = null
        var realtimeSender: UsbRealtimeSender? = null

        try {
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
        } catch (exception: Exception) {
            realtimeSender?.let(broadcaster::removeSink)
            realtimeSender?.close()
            session?.close()
            opened.close()
            throw exception
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
            ).join(
                inputStream = opened.input,
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
