package com.natsx.controller.core.transport.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import android.content.Context
import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.RumblePayload
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import com.natsx.controller.core.trust.TrustedPeerStore

fun interface BluetoothRfcommSocketProvider {
    fun open(): BluetoothRfcommSocket
}

interface BluetoothRfcommSocket : Closeable {
    val inputStream: InputStream
    val outputStream: OutputStream

    fun connect()
}

class AndroidBluetoothRfcommSocket(
    private val socket: BluetoothSocket,
) : BluetoothRfcommSocket {
    override val inputStream: InputStream
        get() = socket.inputStream

    override val outputStream: OutputStream
        get() = socket.outputStream

    override fun connect() {
        socket.connect()
    }

    override fun close() {
        socket.close()
    }
}

interface BluetoothRealtimeLink : RealtimeStateSink, Closeable {
    val lastHeartbeatReceivedNanos: Long

    fun trySendTransportPreference(
        payload: TransportPreferencePayload,
    ): Boolean
}

fun interface BluetoothRealtimeLinkFactory {
    fun create(): BluetoothRealtimeLink
}

class BluetoothRfcommRealtimeLink internal constructor(
    private val socket: BluetoothRfcommSocket,
    private val session: BluetoothTrustedSession,
    private val sender: BluetoothRealtimeSender,
    private val onSessionClosed: () -> Unit = {},
) : BluetoothRealtimeLink {
    private var closed = false

    override val lastHeartbeatReceivedNanos: Long
        get() = sender.lastHeartbeatReceivedNanos

    override fun publish(envelope: RealtimeStateEnvelope) {
        check(!closed) {
            "Bluetooth RFCOMM realtime link is closed."
        }

        sender.publish(envelope)
    }

    override fun trySendTransportPreference(
        payload: TransportPreferencePayload,
    ): Boolean {
        if (closed) {
            return false
        }

        return sender
            .trySendTransportPreference(
                payload,
            )
    }

    override fun close() {
        if (closed) {
            return
        }

        closed = true

        try {
            sender.close()
        } finally {
            try {
                session.close()
            } finally {
                try { socket.close() } finally { onSessionClosed() }
            }
        }
    }
}

/**
 * Opens the production RFCOMM data path and joins Bluetooth to the currently
 * active trusted controller session, or authenticates directly using saved trust.
 *
 * The socket connection itself is transport establishment only. Controller
 * traffic is not trusted until BluetoothSecondarySessionJoinClient verifies
 * the active SessionId/session key and TRANSPORT_READY exchange.
 */
class BluetoothRfcommConnector(
    private val socketProvider: BluetoothRfcommSocketProvider,
    private val secondaryJoinClient: BluetoothSecondarySessionJoinClient,
    private val rumbleSink: (RumblePayload) -> Unit = {},
    private val handoverSink: (HandoverPayload) -> Unit = {},
    private val sessionOpener: ((InputStream, OutputStream) -> BluetoothTrustedSession)? = null,
    private val onSessionClosed: () -> Unit = {},
    private val connectionTimeoutMillis: Long = 15_000,
) : BluetoothRealtimeLinkFactory {
    override fun create(): BluetoothRfcommRealtimeLink = connect()

    fun connect(): BluetoothRfcommRealtimeLink {
        val socket = socketProvider.open()
        val deadline = connectionTimeoutExecutor.schedule(
            { runCatching { socket.close() } }, connectionTimeoutMillis, TimeUnit.MILLISECONDS,
        )

        try {
            socket.connect()

            val session =
                sessionOpener?.invoke(socket.inputStream, socket.outputStream)
                    ?: secondaryJoinClient.join(socket.inputStream, socket.outputStream)

            try {
                val sender =
                    BluetoothRealtimeSender(
                        outputStream = socket.outputStream,
                        trustedSession = session,
                        inputStream = socket.inputStream,
                        rumbleSink = rumbleSink,
                        handoverSink = handoverSink,
                    )

                return BluetoothRfcommRealtimeLink(
                    socket = socket,
                    session = session,
                    sender = sender,
                    onSessionClosed = onSessionClosed,
                )
            } catch (exception: Exception) {
                session.close()
                throw exception
            }
        } catch (exception: Exception) {
            runCatching {
                socket.close()
            }
            onSessionClosed()
            throw exception
        } finally {
            deadline.cancel(false)
        }
    }

    companion object {
        private val connectionTimeoutExecutor = Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(runnable, "natsx-bluetooth-connect-timeout").apply { isDaemon = true }
        }

        val SERVICE_UUID: UUID =
            UUID.fromString(
                "65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11",
            )

        @SuppressLint("MissingPermission")
        fun forDevice(
            context: Context,
            device: BluetoothDevice,
            localPeerId: PeerId,
            receiverPeerId: PeerId,
            sessionRegistry: TrustedSessionRegistry,
            trustedPeerStore: TrustedPeerStore? = null,
            permissionGate: BluetoothPermissionGate =
                BluetoothPermissionGate(context),
            rumbleSink: (RumblePayload) -> Unit = {},
            handoverSink: (HandoverPayload) -> Unit = {},
        ): BluetoothRfcommConnector {
            require(permissionGate.isBluetoothSupported()) {
                "Bluetooth is not supported on this device."
            }
            require(permissionGate.hasRequiredRuntimePermissions()) {
                "Bluetooth runtime permissions are not granted."
            }
            require(permissionGate.isBluetoothEnabled()) {
                "Bluetooth is disabled."
            }
            try {
                require(
                    device.bondState ==
                        BluetoothDevice.BOND_BONDED,
                ) {
                    "Bluetooth receiver is not OS-bonded. Complete pairing first."
                }
            } catch (exception: SecurityException) {
                throw IllegalStateException(
                    "Bluetooth permission was revoked while checking receiver bonding.",
                    exception,
                )
            }

            val manager =
                context.getSystemService(
                    android.bluetooth.BluetoothManager::class.java,
                )
            val adapter: BluetoothAdapter =
                checkNotNull(manager?.adapter) {
                    "Bluetooth adapter is unavailable."
                }

            val provider =
                BluetoothRfcommSocketProvider {
                    try {
                        // Discovery slows RFCOMM setup and is unnecessary when
                        // connecting to an already selected / bonded receiver.
                        runCatching {
                            adapter.cancelDiscovery()
                        }

                        AndroidBluetoothRfcommSocket(
                            device.createRfcommSocketToServiceRecord(
                                SERVICE_UUID,
                            ),
                        )
                    } catch (exception: SecurityException) {
                        throw IllegalStateException(
                            "Bluetooth permission was revoked while opening RFCOMM.",
                            exception,
                        )
                    }
                }

            val sessionConnector = BluetoothSessionConnector(localPeerId, receiverPeerId, sessionRegistry, trustedPeerStore)
            return BluetoothRfcommConnector(
                socketProvider = provider,
                sessionOpener = sessionConnector::connect,
                onSessionClosed = sessionConnector::sessionClosed,
                secondaryJoinClient =
                    BluetoothSecondarySessionJoinClient(
                        localPeerId = localPeerId,
                        receiverPeerId = receiverPeerId,
                        sessionRegistry = sessionRegistry,
                    ),
                rumbleSink = rumbleSink,
                handoverSink = handoverSink,
            )
        }
    }
}
