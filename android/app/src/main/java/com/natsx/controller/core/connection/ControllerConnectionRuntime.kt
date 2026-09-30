package com.natsx.controller.core.connection

import android.bluetooth.BluetoothAdapter
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.haptics.ControllerHapticSink
import com.natsx.controller.core.protocol.CapabilityFlags
import com.natsx.controller.core.protocol.ControlPayloadCodec
import com.natsx.controller.core.protocol.DeviceRole
import com.natsx.controller.core.protocol.HelloPayload
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TransportMask
import com.natsx.controller.core.protocol.TrustState
import com.natsx.controller.security.AndroidTrustedReceiverStore
import com.natsx.controller.security.TrustedReceiverRecord
import com.natsx.controller.transport.bluetooth.BluetoothControllerTransport
import com.natsx.controller.transport.bluetooth.BluetoothFallbackHost
import com.natsx.controller.transport.bluetooth.EstablishedBluetoothSession
import com.natsx.controller.transport.wifi.EstablishedWifiSession
import com.natsx.controller.transport.wifi.WifiControllerTransport
import com.natsx.controller.transport.wifi.WifiDiscoveryClient
import com.natsx.controller.transport.wifi.WifiGamepadFrameSender
import com.natsx.controller.transport.wifi.WifiTrustedSessionClient
import java.net.InetAddress
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

enum class ControllerConnectionState {
    STOPPED,
    PAIRING_REQUIRED,
    DISCOVERING,
    CONNECTING,
    AUTHENTICATING,
    ACTIVE,
    RECOVERING,
}

data class ControllerConnectionStatus(
    val state: ControllerConnectionState,
    val message: String,
    val receiverName: String? = null,
    val hostAddress: String? = null,
    val activeTransport: String? = null,
    val backupTransport: String? = null,
)

class ControllerConnectionRuntime(
    private val androidDeviceId: SessionId,
    private val stateStore: GamepadStateStore,
    private val trustedReceivers: AndroidTrustedReceiverStore,
    private val haptics: ControllerHapticSink,
    private val bluetoothAdapter: BluetoothAdapter? = null,
) : AutoCloseable {
    private val running = AtomicBoolean(false)

    private val wifiLastInboundNanos =
        AtomicLong(0)
    private val wifiFailed =
        AtomicBoolean(false)

    private val bluetoothLastInboundNanos =
        AtomicLong(0)
    private val bluetoothFailed =
        AtomicBoolean(false)

    @Volatile
    private var wifiWorker: Thread? = null

    @Volatile
    private var bluetoothWorker: Thread? = null

    @Volatile
    private var activeWifiTransport:
        WifiControllerTransport? = null

    @Volatile
    private var activeWifiPublisher:
        RealtimeGamepadPublisher? = null

    @Volatile
    private var bluetoothHost:
        BluetoothFallbackHost? = null

    @Volatile
    private var standbyBluetoothTransport:
        BluetoothControllerTransport? = null

    @Volatile
    private var standbyBluetoothPublisher:
        RealtimeGamepadPublisher? = null

    @Volatile
    var status = ControllerConnectionStatus(
        ControllerConnectionState.STOPPED,
        "Controller connection is stopped.",
    )
        private set

    @Volatile
    var onStatusChanged:
        ((ControllerConnectionStatus) -> Unit)? = null

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        startBluetoothFallbackIfPossible()

        wifiWorker = Thread(
            { runWifiLoop() },
            "natsx-wifi-connection-runtime",
        ).apply {
            isDaemon = true
            start()
        }
    }

    fun refreshBluetoothFallback() {
        if (!running.get()) {
            return
        }

        if (bluetoothHost != null) {
            return
        }

        startBluetoothFallbackIfPossible()
    }

    fun stop() {
        if (!running.getAndSet(false)) {
            return
        }

        activeWifiPublisher?.close()
        activeWifiPublisher = null

        activeWifiTransport?.close()
        activeWifiTransport = null

        standbyBluetoothPublisher?.close()
        standbyBluetoothPublisher = null

        standbyBluetoothTransport?.close()
        standbyBluetoothTransport = null

        bluetoothHost?.close()
        bluetoothHost = null

        haptics.stopRumble()

        wifiWorker?.interrupt()
        wifiWorker = null

        bluetoothWorker?.interrupt()
        bluetoothWorker = null

        updateStatus(
            ControllerConnectionState.STOPPED,
            "Controller connection is stopped.",
        )
    }

    override fun close() {
        stop()
    }

    private fun runWifiLoop() {
        var recoveryDelayMs = 100L

        while (running.get()) {
            val trustedIds =
                trustedReceivers.listTrustedDeviceIds()

            if (trustedIds.isEmpty()) {
                updateStatus(
                    ControllerConnectionState.PAIRING_REQUIRED,
                    "Pair a Windows receiver to start playing.",
                    backupTransport =
                        bluetoothBackupLabel(),
                )
                sleepInterruptibly(750)
                continue
            }

            var connected = false

            for (windowsDeviceId in trustedIds) {
                if (!running.get()) {
                    break
                }

                val record = runCatching {
                    trustedReceivers.tryGet(
                        windowsDeviceId,
                    )
                }.getOrNull() ?: continue

                try {
                    connected =
                        tryTrustedReceiver(record)
                } finally {
                    record.pairingRootKey.fill(0)
                }

                if (connected) {
                    recoveryDelayMs = 100L
                    break
                }
            }

            if (!running.get()) {
                break
            }

            if (!connected) {
                if (standbyBluetoothTransport
                        ?.isRunning == true
                ) {
                    updateStatus(
                        ControllerConnectionState.ACTIVE,
                        "Wi-Fi unavailable. Bluetooth fallback is ready.",
                        activeTransport = "Bluetooth",
                    )
                } else {
                    updateStatus(
                        ControllerConnectionState.RECOVERING,
                        "Receiver unavailable. Retrying automatically.",
                    )
                }

                sleepInterruptibly(recoveryDelayMs)
                recoveryDelayMs = when {
                    recoveryDelayMs < 250L -> 250L
                    recoveryDelayMs < 500L -> 500L
                    else -> 750L
                }
            }
        }
    }

    private fun tryTrustedReceiver(
        record: TrustedReceiverRecord,
    ): Boolean {
        val directHost = record.lastHostAddress

        if (!directHost.isNullOrBlank()) {
            updateStatus(
                ControllerConnectionState.CONNECTING,
                "Trying last receiver endpoint.",
                receiverName = record.displayName,
                hostAddress = directHost,
                backupTransport =
                    bluetoothBackupLabel(),
            )

            val directAddress = runCatching {
                InetAddress.getByName(directHost)
            }.getOrNull()

            if (directAddress != null &&
                tryEndpoint(
                    record,
                    directAddress,
                    record.lastPort,
                )
            ) {
                return true
            }
        }

        updateStatus(
            ControllerConnectionState.DISCOVERING,
            "Searching local network for trusted receiver.",
            receiverName = record.displayName,
            backupTransport =
                bluetoothBackupLabel(),
        )

        val discovered = runCatching {
            WifiDiscoveryClient(androidDeviceId)
                .discover(DISCOVERY_TIMEOUT_MS)
        }.getOrDefault(emptyList())

        val matching =
            discovered.firstOrNull {
                it.response.windowsDeviceId ==
                    record.windowsDeviceId
            } ?: return false

        return tryEndpoint(
            record,
            matching.address,
            matching.response.controllerPort,
            matching.response.receiverName,
        )
    }

    private fun tryEndpoint(
        record: TrustedReceiverRecord,
        address: InetAddress,
        port: Int,
        discoveredName: String? =
            record.displayName,
    ): Boolean {
        if (!running.get()) {
            return false
        }

        updateStatus(
            ControllerConnectionState.AUTHENTICATING,
            "Authenticating trusted receiver.",
            receiverName = discoveredName,
            hostAddress = address.hostAddress,
            backupTransport =
                bluetoothBackupLabel(),
        )

        val androidHello = HelloPayload(
            deviceId = androidDeviceId,
            role = DeviceRole.ANDROID_CONTROLLER,
            transports =
                TransportMask.USB or
                    TransportMask.WIFI or
                    TransportMask.BLUETOOTH,
            capabilities =
                CapabilityFlags.RUMBLE or
                    CapabilityFlags.GUIDE or
                    CapabilityFlags.COMPETITIVE_240_HZ or
                    CapabilityFlags.WARM_STANDBY,
            minimumMajor = 1,
            maximumMajor = 1,
            maximumMinor = 0,
            trustState = TrustState.PAIRED,
        )

        val sessionClient =
            WifiTrustedSessionClient(
                androidHello = androidHello,
                expectedWindowsDeviceId =
                    record.windowsDeviceId,
                pairingRootKey =
                    record.pairingRootKey,
            )

        val established = try {
            sessionClient.connect(
                remoteAddress = address,
                remotePort = port,
            )
        } catch (_: Throwable) {
            sessionClient.close()
            return false
        }

        sessionClient.close()

        trustedReceivers.updateEndpoint(
            windowsDeviceId =
                record.windowsDeviceId,
            displayName = discoveredName,
            hostAddress =
                address.hostAddress.orEmpty(),
            port = port,
        )

        return runActiveWifiSession(
            established,
            discoveredName,
        )
    }

    private fun runActiveWifiSession(
        established: EstablishedWifiSession,
        receiverName: String?,
    ): Boolean {
        val session =
            established.trustedSession

        wifiFailed.set(false)
        wifiLastInboundNanos.set(
            System.nanoTime(),
        )

        val transport = WifiControllerTransport(
            remoteAddress =
                established.remoteAddress,
            remotePort =
                established.remotePort,
            sessionId =
                session.sessionId,
            sessionKey =
                session.sessionKey,
        )

        transport.onFrameReceived = { frame ->
            wifiLastInboundNanos.set(
                System.nanoTime(),
            )
            handleOutputFrame(frame)
        }

        transport.onTransportError = {
            wifiFailed.set(true)
        }

        return try {
            transport.start()

            val publisher =
                RealtimeGamepadPublisher(
                    stateStore = stateStore,
                    sender =
                        WifiGamepadFrameSender(
                            transport =
                                transport,
                            sessionId =
                                session.sessionId,
                        ),
                    rateHz = 120,
                    onError = {
                        wifiFailed.set(true)
                    },
                )

            activeWifiTransport =
                transport
            activeWifiPublisher =
                publisher

            publisher.start()

            updateStatus(
                ControllerConnectionState.ACTIVE,
                "Connected over Wi-Fi.",
                receiverName = receiverName,
                hostAddress =
                    established
                        .remoteAddress
                        .hostAddress,
                activeTransport = "Wi-Fi",
                backupTransport =
                    bluetoothBackupLabel(),
            )

            while (
                running.get() &&
                !wifiFailed.get()
            ) {
                val silenceMs =
                    (
                        System.nanoTime() -
                            wifiLastInboundNanos.get()
                    ) / 1_000_000L

                if (silenceMs >=
                    WIFI_INBOUND_SILENCE_RECOVERY_MS
                ) {
                    break
                }

                sleepInterruptibly(
                    ACTIVE_MONITOR_INTERVAL_MS,
                )
            }

            running.get()
        } finally {
            activeWifiPublisher?.close()
            activeWifiPublisher = null

            activeWifiTransport?.close()
            activeWifiTransport = null

            haptics.stopRumble()
            session.sessionKey.fill(0)

            if (running.get()) {
                if (standbyBluetoothTransport
                        ?.isRunning == true
                ) {
                    updateStatus(
                        ControllerConnectionState.ACTIVE,
                        "Wi-Fi session lost. Bluetooth fallback remains connected.",
                        receiverName =
                            receiverName,
                        activeTransport =
                            "Bluetooth",
                    )
                } else {
                    updateStatus(
                        ControllerConnectionState.RECOVERING,
                        "Wi-Fi session lost. Reconnecting automatically.",
                        receiverName =
                            receiverName,
                        hostAddress =
                            established
                                .remoteAddress
                                .hostAddress,
                    )
                }
            }
        }
    }

    private fun startBluetoothFallbackIfPossible() {
        val adapter =
            bluetoothAdapter ?: return

        val host =
            BluetoothFallbackHost(
                adapter = adapter,
                androidDeviceId =
                    androidDeviceId,
                trustResolver = {
                    windowsDeviceId ->
                    runCatching {
                        trustedReceivers
                            .tryGet(
                                windowsDeviceId,
                            )
                            ?.pairingRootKey
                    }.getOrNull()
                },
            )

        try {
            host.start()
        } catch (_: Throwable) {
            host.close()
            return
        }

        bluetoothHost = host

        bluetoothWorker = Thread(
            { runBluetoothLoop(host) },
            "natsx-bluetooth-fallback-runtime",
        ).apply {
            isDaemon = true
            start()
        }
    }

    private fun runBluetoothLoop(
        host: BluetoothFallbackHost,
    ) {
        while (running.get()) {
            val established =
                try {
                    host.pollEstablished(
                        BLUETOOTH_POLL_MS,
                    )
                } catch (_: InterruptedException) {
                    Thread.currentThread()
                        .interrupt()
                    break
                }

            if (established == null) {
                continue
            }

            runBluetoothSession(
                established,
            )
        }
    }

    private fun runBluetoothSession(
        established:
            EstablishedBluetoothSession,
    ) {
        val session =
            established.trustedSession

        bluetoothFailed.set(false)
        bluetoothLastInboundNanos.set(
            System.nanoTime(),
        )

        val transport =
            BluetoothControllerTransport(
                connection =
                    established.connection,
                session = session,
            )

        transport.onFrameReceived = { frame ->
            bluetoothLastInboundNanos.set(
                System.nanoTime(),
            )
            handleOutputFrame(frame)
        }

        transport.onTransportError = {
            bluetoothFailed.set(true)
        }

        try {
            transport.start()

            val publisher =
                RealtimeGamepadPublisher(
                    stateStore = stateStore,
                    sender = transport,
                    rateHz = 120,
                    onError = {
                        bluetoothFailed.set(true)
                    },
                )

            standbyBluetoothTransport =
                transport
            standbyBluetoothPublisher =
                publisher

            publisher.start()

            if (activeWifiTransport == null) {
                updateStatus(
                    ControllerConnectionState.ACTIVE,
                    "Connected over Bluetooth fallback.",
                    receiverName =
                        trustedReceivers
                            .listMetadata()
                            .firstOrNull {
                                it.windowsDeviceId ==
                                    session.windowsDeviceId
                            }
                            ?.displayName,
                    activeTransport =
                        "Bluetooth",
                )
            } else {
                publishCurrentStatusWithBackup()
            }

            while (
                running.get() &&
                transport.isRunning &&
                !bluetoothFailed.get()
            ) {
                val silenceMs =
                    (
                        System.nanoTime() -
                            bluetoothLastInboundNanos
                                .get()
                    ) / 1_000_000L

                if (silenceMs >=
                    BLUETOOTH_INBOUND_SILENCE_RECOVERY_MS
                ) {
                    break
                }

                sleepInterruptibly(
                    ACTIVE_MONITOR_INTERVAL_MS,
                )
            }
        } finally {
            standbyBluetoothPublisher
                ?.close()
            standbyBluetoothPublisher = null

            standbyBluetoothTransport
                ?.close()
            standbyBluetoothTransport = null

            session.sessionKey.fill(0)

            if (running.get() &&
                activeWifiTransport == null
            ) {
                updateStatus(
                    ControllerConnectionState.RECOVERING,
                    "Bluetooth fallback lost. Retrying Wi-Fi and Bluetooth automatically.",
                )
            }
        }
    }

    private fun handleOutputFrame(
        frame:
            com.natsx.controller.core.protocol
                .ProtocolFrame,
    ) {
        if (frame.messageType !=
            MessageType.RUMBLE
        ) {
            return
        }

        runCatching {
            ControlPayloadCodec.decodeRumble(
                frame.payload,
            )
        }.getOrNull()?.let { rumble ->
            haptics.applyRumble(
                lowFrequency =
                    rumble.lowFrequency,
                highFrequency =
                    rumble.highFrequency,
                durationMilliseconds =
                    rumble.durationMilliseconds,
            )
        }
    }

    private fun publishCurrentStatusWithBackup() {
        val current = status

        updateStatus(
            current.state,
            current.message,
            receiverName =
                current.receiverName,
            hostAddress =
                current.hostAddress,
            activeTransport =
                current.activeTransport ?:
                    "Wi-Fi",
            backupTransport =
                bluetoothBackupLabel(),
        )
    }

    private fun bluetoothBackupLabel():
        String? =
        if (standbyBluetoothTransport
                ?.isRunning == true
        ) {
            "Bluetooth READY"
        } else {
            null
        }

    private fun updateStatus(
        state: ControllerConnectionState,
        message: String,
        receiverName: String? = null,
        hostAddress: String? = null,
        activeTransport: String? = null,
        backupTransport: String? = null,
    ) {
        val next = ControllerConnectionStatus(
            state = state,
            message = message,
            receiverName = receiverName,
            hostAddress = hostAddress,
            activeTransport =
                activeTransport,
            backupTransport =
                backupTransport,
        )

        status = next
        onStatusChanged?.invoke(next)
    }

    private fun sleepInterruptibly(
        milliseconds: Long,
    ) {
        try {
            Thread.sleep(milliseconds)
        } catch (_: InterruptedException) {
            Thread.currentThread()
                .interrupt()
        }
    }

    private companion object {
        const val DISCOVERY_TIMEOUT_MS = 800
        const val ACTIVE_MONITOR_INTERVAL_MS =
            40L
        const val WIFI_INBOUND_SILENCE_RECOVERY_MS =
            250L
        const val BLUETOOTH_INBOUND_SILENCE_RECOVERY_MS =
            1_000L
        const val BLUETOOTH_POLL_MS =
            500L
    }
}
