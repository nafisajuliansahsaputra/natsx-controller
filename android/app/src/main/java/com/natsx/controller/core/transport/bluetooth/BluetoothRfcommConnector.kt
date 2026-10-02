package com.natsx.controller.core.transport.bluetooth

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
                socket.close()
            }
        }
    }
}

/**
 * Opens the production RFCOMM data path and joins Bluetooth to the currently
 * active trusted controller session.
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
) : BluetoothRealtimeLinkFactory {
    override fun create(): BluetoothRfcommRealtimeLink = connect()

    fun connect(): BluetoothRfcommRealtimeLink {
        val socket = socketProvider.open()

        try {
            socket.connect()

            val session =
                secondaryJoinClient.join(
                    inputStream = socket.inputStream,
                    outputStream = socket.outputStream,
                )

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
                )
            } catch (exception: Exception) {
                session.close()
                throw exception
            }
        } catch (exception: Exception) {
            runCatching {
                socket.close()
            }
            throw exception
        }
    }

    companion object {
        val SERVICE_UUID: UUID =
            UUID.fromString(
                "65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11",
            )

        fun forDevice(
            context: Context,
            device: BluetoothDevice,
            localPeerId: PeerId,
            receiverPeerId: PeerId,
            sessionRegistry: TrustedSessionRegistry,
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
            require(
                device.bondState ==
                    BluetoothDevice.BOND_BONDED,
            ) {
                "Bluetooth receiver is not OS-bonded. Complete pairing first."
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
                }

            return BluetoothRfcommConnector(
                socketProvider = provider,
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
