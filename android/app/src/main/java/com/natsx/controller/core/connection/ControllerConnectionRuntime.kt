package com.natsx.controller.core.connection

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
)

class ControllerConnectionRuntime(
    private val androidDeviceId: SessionId,
    private val stateStore: GamepadStateStore,
    private val trustedReceivers: AndroidTrustedReceiverStore,
    private val haptics: ControllerHapticSink,
) : AutoCloseable {
    private val running = AtomicBoolean(false)
    private val lastInboundNanos = AtomicLong(0)
    private val transportFailed = AtomicBoolean(false)

    @Volatile
    private var worker: Thread? = null

    @Volatile
    private var activeTransport: WifiControllerTransport? = null

    @Volatile
    private var activePublisher: LatestGamepadPublisher? = null

    @Volatile
    var status = ControllerConnectionStatus(
        ControllerConnectionState.STOPPED,
        "Controller connection is stopped.",
    )
        private set

    @Volatile
    var onStatusChanged: ((ControllerConnectionStatus) -> Unit)? = null

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        worker = Thread(
            { runLoop() },
            "natsx-connection-runtime",
        ).apply {
            isDaemon = true
            start()
        }
    }

    fun stop() {
        if (!running.getAndSet(false)) {
            return
        }

        activePublisher?.close()
        activePublisher = null

        activeTransport?.close()
        activeTransport = null
        haptics.stopRumble()

        worker?.interrupt()
        worker = null

        updateStatus(
            ControllerConnectionState.STOPPED,
            "Controller connection is stopped.",
        )
    }

    override fun close() {
        stop()
    }

    private fun runLoop() {
        var recoveryDelayMs = 100L

        while (running.get()) {
            val trustedIds = trustedReceivers.listTrustedDeviceIds()

            if (trustedIds.isEmpty()) {
                updateStatus(
                    ControllerConnectionState.PAIRING_REQUIRED,
                    "Pair a Windows receiver to start playing.",
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
                    trustedReceivers.tryGet(windowsDeviceId)
                }.getOrNull() ?: continue

                try {
                    connected = tryTrustedReceiver(record)
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
                updateStatus(
                    ControllerConnectionState.RECOVERING,
                    "Receiver unavailable. Retrying automatically.",
                )

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
        )

        val discovered = runCatching {
            WifiDiscoveryClient(androidDeviceId)
                .discover(DISCOVERY_TIMEOUT_MS)
        }.getOrDefault(emptyList())

        val matching =
            discovered.firstOrNull {
                it.response.windowsDeviceId == record.windowsDeviceId
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
        discoveredName: String? = record.displayName,
    ): Boolean {
        if (!running.get()) {
            return false
        }

        updateStatus(
            ControllerConnectionState.AUTHENTICATING,
            "Authenticating trusted receiver.",
            receiverName = discoveredName,
            hostAddress = address.hostAddress,
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

        val sessionClient = WifiTrustedSessionClient(
            androidHello = androidHello,
            expectedWindowsDeviceId = record.windowsDeviceId,
            pairingRootKey = record.pairingRootKey,
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
            windowsDeviceId = record.windowsDeviceId,
            displayName = discoveredName,
            hostAddress = address.hostAddress.orEmpty(),
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
        val session = established.trustedSession
        transportFailed.set(false)
        lastInboundNanos.set(System.nanoTime())

        val transport = WifiControllerTransport(
            remoteAddress = established.remoteAddress,
            remotePort = established.remotePort,
            sessionId = session.sessionId,
            sessionKey = session.sessionKey,
        )

        transport.onFrameReceived = { frame ->
            lastInboundNanos.set(System.nanoTime())

            if (frame.messageType == MessageType.RUMBLE) {
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
        }

        transport.onTransportError = {
            transportFailed.set(true)
        }

        return try {
            transport.start()

            val publisher = LatestGamepadPublisher(
                stateStore = stateStore,
                sender = WifiGamepadFrameSender(
                    transport = transport,
                    sessionId = session.sessionId,
                ),
            )

            activeTransport = transport
            activePublisher = publisher

            publisher.start(sendInitialState = true)

            updateStatus(
                ControllerConnectionState.ACTIVE,
                "Connected over Wi-Fi.",
                receiverName = receiverName,
                hostAddress = established.remoteAddress.hostAddress,
            )

            while (running.get() && !transportFailed.get()) {
                val silenceMs =
                    (System.nanoTime() - lastInboundNanos.get()) /
                        1_000_000L

                if (silenceMs >= INBOUND_SILENCE_RECOVERY_MS) {
                    break
                }

                sleepInterruptibly(ACTIVE_MONITOR_INTERVAL_MS)
            }

            running.get()
        } finally {
            activePublisher?.close()
            activePublisher = null

            activeTransport?.close()
            activeTransport = null
            haptics.stopRumble()

            session.sessionKey.fill(0)

            if (running.get()) {
                updateStatus(
                    ControllerConnectionState.RECOVERING,
                    "Wi-Fi session lost. Reconnecting automatically.",
                    receiverName = receiverName,
                    hostAddress = established.remoteAddress.hostAddress,
                )
            }
        }
    }

    private fun updateStatus(
        state: ControllerConnectionState,
        message: String,
        receiverName: String? = null,
        hostAddress: String? = null,
    ) {
        val next = ControllerConnectionStatus(
            state = state,
            message = message,
            receiverName = receiverName,
            hostAddress = hostAddress,
        )

        status = next
        onStatusChanged?.invoke(next)
    }

    private fun sleepInterruptibly(milliseconds: Long) {
        try {
            Thread.sleep(milliseconds)
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }

    private companion object {
        const val DISCOVERY_TIMEOUT_MS = 800
        const val ACTIVE_MONITOR_INTERVAL_MS = 40L
        const val INBOUND_SILENCE_RECOVERY_MS = 250L
    }
}
