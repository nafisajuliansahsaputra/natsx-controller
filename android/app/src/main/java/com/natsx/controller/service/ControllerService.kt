package com.natsx.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.IBinder
import com.natsx.controller.NatsxControllerApplication
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.session.ControllerRealtimePublisher
import com.natsx.controller.core.transport.bluetooth.BluetoothAutoReconnectRuntime
import com.natsx.controller.core.transport.bluetooth.BluetoothPermissionGate
import com.natsx.controller.core.transport.bluetooth.BluetoothRealtimeLink
import com.natsx.controller.core.transport.bluetooth.BluetoothRealtimeLinkFactory
import com.natsx.controller.core.transport.bluetooth.BluetoothReconnectState
import com.natsx.controller.core.transport.bluetooth.BluetoothRfcommConnector
import com.natsx.controller.core.transport.usb.UsbAccessoryConnector
import com.natsx.controller.core.transport.usb.UsbAccessoryIdentity
import com.natsx.controller.core.transport.usb.UsbAccessoryRuntime
import com.natsx.controller.core.transport.wifi.TrustedReceiverHelloProbe
import com.natsx.controller.core.transport.wifi.TrustedWifiRealtimeLinkFactory
import com.natsx.controller.core.transport.wifi.WifiAutoReconnectRuntime
import com.natsx.controller.core.transport.wifi.WifiDiscoveryClient
import com.natsx.controller.core.transport.wifi.WifiEndpointResolver
import com.natsx.controller.core.transport.wifi.WifiFirstPairingClient
import com.natsx.controller.core.transport.wifi.WifiReconnectState
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

class ControllerService : Service() {
    private lateinit var app: NatsxControllerApplication
    private lateinit var realtimePublisher: ControllerRealtimePublisher
    private lateinit var usbManager: UsbManager
    private lateinit var usbRuntime: UsbAccessoryRuntime
    private val connectionExecutor =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-connection-bootstrap").apply {
                isDaemon = true
            }
        }
    private val connectionBootstrapStarted = AtomicBoolean(false)
    private val usbPermissionRequestInFlight = AtomicBoolean(false)
    private val wifiRuntimeGate = Any()
    private val bluetoothRuntimeGate = Any()
    private val usbAttachWatcher: ScheduledExecutorService =
        Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(runnable, "natsx-usb-attach-watcher").apply {
                isDaemon = true
            }
        }

    @Volatile
    private var wifiRuntime: WifiAutoReconnectRuntime? = null

    @Volatile
    private var bluetoothRuntime: BluetoothAutoReconnectRuntime? = null

    private val usbReceiver =
        object : BroadcastReceiver() {
            override fun onReceive(
                context: Context,
                intent: Intent,
            ) {
                val accessory =
                    if (Build.VERSION.SDK_INT >= 33) {
                        intent.getParcelableExtra(
                            UsbManager.EXTRA_ACCESSORY,
                            UsbAccessory::class.java,
                        )
                    } else {
                        @Suppress("DEPRECATION")
                        intent.getParcelableExtra(
                            UsbManager.EXTRA_ACCESSORY,
                        )
                    }

                when (intent.action) {
                    ACTION_USB_PERMISSION -> {
                        usbPermissionRequestInFlight.set(false)

                        if (
                            intent.getBooleanExtra(
                                UsbManager.EXTRA_PERMISSION_GRANTED,
                                false,
                            ) &&
                            accessory != null &&
                            UsbAccessoryIdentity.matches(accessory)
                        ) {
                            app.usbRuntimeStatus.publish(
                                "USB permission granted. Connecting…",
                            )
                            usbRuntime.connect(accessory)
                        } else {
                            app.usbRuntimeStatus.publish(
                                "USB permission was denied.",
                                isError = true,
                            )
                        }
                    }

                    UsbManager.ACTION_USB_ACCESSORY_ATTACHED -> {
                        if (
                            accessory != null &&
                            UsbAccessoryIdentity.matches(accessory)
                        ) {
                            app.usbRuntimeStatus.publish(
                                "USB accessory attached. Preparing uplink…",
                            )
                            connectUsbAccessory(accessory)
                        }
                    }

                    UsbManager.ACTION_USB_ACCESSORY_DETACHED -> {
                        usbPermissionRequestInFlight.set(false)

                        if (
                            accessory != null &&
                            UsbAccessoryIdentity.matches(accessory)
                        ) {
                            usbRuntime.disconnect(accessory)
                        }
                    }
                }
            }
        }

    override fun onCreate() {
        super.onCreate()

        app = application as NatsxControllerApplication
        realtimePublisher = ControllerRealtimePublisher(
            stateStore = app.gamepadStateStore,
            broadcaster = app.realtimeBroadcaster,
        )
        realtimePublisher.start()

        usbManager =
            getSystemService(UsbManager::class.java)

        usbRuntime =
            UsbAccessoryRuntime(
                usbManager = usbManager,
                localPeerId = app.localPeerId,
                trustedPeerStore = app.trustedPeerStore,
                sessionRegistry = app.trustedSessionRegistry,
                broadcaster = app.realtimeBroadcaster,
                pairingConfirmation = app.pairingConfirmation,
                status = app.usbRuntimeStatus,
                rumbleSink =
                    app.hapticEngine::handleGameRumble,
            )

        registerUsbReceiver()
        ensureConnectionBootstrap()
        startUsbAttachWatcher()

        val notificationManager = getSystemService(NotificationManager::class.java)
        notificationManager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "Controller connection",
                NotificationManager.IMPORTANCE_LOW,
            ),
        )

        val notification = Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle("NATSX Controller")
            .setContentText("Controller runtime is active")
            .setOngoing(true)
            .build()

        startForeground(NOTIFICATION_ID, notification)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        ensureConnectionBootstrap()
        return START_STICKY
    }

    override fun onDestroy() {
        synchronized(bluetoothRuntimeGate) {
            bluetoothRuntime?.close()
            bluetoothRuntime = null
        }

        synchronized(wifiRuntimeGate) {
            wifiRuntime?.close()
            wifiRuntime = null
        }
        connectionExecutor.shutdownNow()
        usbAttachWatcher.shutdownNow()

        if (::usbRuntime.isInitialized) {
            usbRuntime.close()
        }

        runCatching {
            unregisterReceiver(usbReceiver)
        }

        if (::realtimePublisher.isInitialized) {
            realtimePublisher.close()
        }

        if (::app.isInitialized) {
            app.hapticEngine.stopGameRumble()
        }

        super.onDestroy()
    }

    private fun ensureConnectionBootstrap() {
        if (!connectionBootstrapStarted.compareAndSet(false, true)) {
            return
        }

        connectionExecutor.execute {
            try {
                bootstrapTrustedConnection()
            } finally {
                if (wifiRuntime == null) {
                    connectionBootstrapStarted.set(false)
                }
            }
        }
    }

    private fun bootstrapTrustedConnection() {
        var retryIndex = 0

        while (!Thread.currentThread().isInterrupted) {
            try {
                val peers =
                    app.trustedPeerStore.list()

                val peer =
                    when {
                        peers.isEmpty() -> {
                            app.usbRuntimeStatus.publish(
                                "No trusted PC yet. Secure pairing is using LAN/Wi-Fi…",
                            )

                            WifiFirstPairingClient(
                                localPeerId = app.localPeerId,
                                trustedPeerStore = app.trustedPeerStore,
                                pairingConfirmation = app.pairingConfirmation,
                            ).pair().also {
                                app.usbRuntimeStatus.publish(
                                    "LAN pairing complete. Establishing trusted Wi-Fi session…",
                                )
                            }
                        }

                        peers.size == 1 ->
                            peers.single()

                        else -> {
                            app.usbRuntimeStatus.publish(
                                "Multiple trusted PCs found. Receiver selection is required.",
                                isError = true,
                            )
                            return
                        }
                    }

                startTrustedWifi(peer.peerId)
                startTrustedBluetooth(peer.peerId)

                if (waitForTrustedSession(peer.peerId)) {
                    app.usbRuntimeStatus.publish(
                        "Trusted Wi-Fi session active. Attaching USB as low-latency uplink…",
                    )

                    connectUsbIfPresent()
                } else {
                    app.usbRuntimeStatus.publish(
                        "Wi-Fi trust is saved; waiting for receiver session. USB will attach after reconnect.",
                    )
                }

                return
            } catch (exception: Exception) {
                val detail =
                    exception.message
                        ?.takeIf { it.isNotBlank() }
                        ?: exception::class.java.simpleName

                app.usbRuntimeStatus.publish(
                    "LAN connection retry: $detail",
                    isError = true,
                )

                val delay =
                    RETRY_BACKOFF_MILLIS[
                        retryIndex.coerceAtMost(
                            RETRY_BACKOFF_MILLIS.lastIndex,
                        )
                    ]

                retryIndex += 1

                try {
                    Thread.sleep(delay)
                } catch (_: InterruptedException) {
                    Thread.currentThread().interrupt()
                    return
                }
            }
        }
    }

    private fun startTrustedWifi(
        receiverPeerId: com.natsx.controller.core.protocol.PeerId,
    ) {
        synchronized(wifiRuntimeGate) {
            val existing =
                wifiRuntime

            if (
                existing != null &&
                existing.state != WifiReconnectState.STOPPED
            ) {
                return
            }

            existing?.close()
            wifiRuntime = null

            val resolver =
                WifiEndpointResolver(
                receiverPeerId = receiverPeerId,
                endpointCache = app.wifiEndpointCache,
                endpointProbe =
                    TrustedReceiverHelloProbe(
                        localPeerId = app.localPeerId,
                        expectedReceiverPeerId = receiverPeerId,
                    ),
                discovery =
                    WifiDiscoveryClient(
                        localPeerId = app.localPeerId,
                    ),
            )

        val linkFactory =
            TrustedWifiRealtimeLinkFactory(
                localPeerId = app.localPeerId,
                receiverPeerId = receiverPeerId,
                trustedPeerStore = app.trustedPeerStore,
                sessionRegistry = app.trustedSessionRegistry,
                rumbleSink =
                    app.hapticEngine::handleGameRumble,
            )

        val runtime =
            WifiAutoReconnectRuntime(
                broadcaster = app.realtimeBroadcaster,
                endpointProvider = resolver,
                linkFactory = linkFactory,
            )

            wifiRuntime = runtime
            runtime.start()
        }
    }

    private fun startTrustedBluetooth(
        receiverPeerId: PeerId,
    ) {
        synchronized(bluetoothRuntimeGate) {
            val permissionGate =
                BluetoothPermissionGate(this)

            if (
                !permissionGate.isBluetoothSupported() ||
                !permissionGate.hasRequiredRuntimePermissions() ||
                !permissionGate.isBluetoothEnabled()
            ) {
                return
            }

            val existing =
                bluetoothRuntime

            if (
                existing != null &&
                existing.state != BluetoothReconnectState.STOPPED
            ) {
                return
            }

            existing?.close()
            bluetoothRuntime = null

            val runtime =
                BluetoothAutoReconnectRuntime(
                    broadcaster = app.realtimeBroadcaster,
                    linkFactory =
                        BluetoothRealtimeLinkFactory {
                            createBondedBluetoothLink(
                                receiverPeerId,
                            )
                        },
                )

            bluetoothRuntime = runtime
            runtime.start()
        }
    }

    private fun createBondedBluetoothLink(
        receiverPeerId: PeerId,
    ): BluetoothRealtimeLink {
        val permissionGate =
            BluetoothPermissionGate(this)

        require(permissionGate.isBluetoothSupported()) {
            "Bluetooth is not supported on this device."
        }
        require(permissionGate.hasRequiredRuntimePermissions()) {
            "Bluetooth runtime permissions are not granted."
        }
        require(permissionGate.isBluetoothEnabled()) {
            "Bluetooth is disabled."
        }

        val manager =
            getSystemService(
                BluetoothManager::class.java,
            )
        val adapter =
            checkNotNull(manager?.adapter) {
                "Bluetooth adapter is unavailable."
            }

        val candidates =
            adapter.bondedDevices
                .filter {
                    it.bondState ==
                        BluetoothDevice.BOND_BONDED
                }
                .sortedBy {
                    it.address
                }

        require(candidates.isNotEmpty()) {
            "No OS-bonded Bluetooth devices are available."
        }

        var lastFailure: Exception? = null

        candidates.forEach { device ->
            try {
                return BluetoothRfcommConnector
                    .forDevice(
                        context = this,
                        device = device,
                        localPeerId = app.localPeerId,
                        receiverPeerId = receiverPeerId,
                        sessionRegistry =
                            app.trustedSessionRegistry,
                        permissionGate =
                            permissionGate,
                        rumbleSink =
                            app.hapticEngine::handleGameRumble,
                    )
                    .connect()
            } catch (exception: Exception) {
                lastFailure = exception
            }
        }

        throw IllegalStateException(
            "No bonded Bluetooth device accepted the authenticated NATSX RFCOMM session.",
            lastFailure,
        )
    }

    private fun waitForTrustedSession(
        receiverPeerId: com.natsx.controller.core.protocol.PeerId,
    ): Boolean {
        repeat(80) {
            app.trustedSessionRegistry
                .get(receiverPeerId)
                ?.use {
                    return true
                }

            try {
                Thread.sleep(100)
            } catch (_: InterruptedException) {
                Thread.currentThread().interrupt()
                return false
            }
        }

        return false
    }

    private fun registerUsbReceiver() {
        val filter =
            IntentFilter().apply {
                addAction(ACTION_USB_PERMISSION)
                addAction(
                    UsbManager.ACTION_USB_ACCESSORY_ATTACHED,
                )
                addAction(
                    UsbManager.ACTION_USB_ACCESSORY_DETACHED,
                )
            }

        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(
                usbReceiver,
                filter,
                RECEIVER_NOT_EXPORTED,
            )
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(
                usbReceiver,
                filter,
            )
        }
    }

    private fun startUsbAttachWatcher() {
        usbAttachWatcher.scheduleWithFixedDelay(
            {
                runCatching {
                    val trustedPeers =
                        app.trustedPeerStore.list()

                    if (trustedPeers.size == 1) {
                        val receiverPeerId =
                            trustedPeers.single().peerId

                        startTrustedWifi(
                            receiverPeerId,
                        )
                        startTrustedBluetooth(
                            receiverPeerId,
                        )
                    }

                    val accessory =
                        UsbAccessoryConnector
                            .findNatsxAccessory(
                                usbManager,
                            )

                    if (usbRuntime.isConnected()) {
                        if (accessory == null) {
                            usbPermissionRequestInFlight.set(false)
                            app.usbRuntimeStatus.publish(
                                "USB accessory disappeared. Falling back to Wi-Fi…",
                            )
                            usbRuntime.disconnect()
                        }

                        return@runCatching
                    }

                    if (
                        accessory != null &&
                        app.trustedPeerStore.list().isNotEmpty() &&
                        app.trustedSessionRegistry.hasAnyActiveSession()
                    ) {
                        connectUsbAccessory(
                            accessory,
                        )
                    }
                }
            },
            0,
            USB_ATTACH_POLL_MILLIS,
            TimeUnit.MILLISECONDS,
        )
    }

    private fun connectUsbIfPresent(
        publishMissing: Boolean = true,
    ) {
        val accessory =
            UsbAccessoryConnector.findNatsxAccessory(
                usbManager,
            )

        if (accessory == null) {
            usbPermissionRequestInFlight.set(false)

            if (publishMissing) {
                app.usbRuntimeStatus.publish(
                    "NATSX USB accessory not detected.",
                )
            }
            return
        }

        connectUsbAccessory(accessory)
    }

    private fun connectUsbAccessory(
        accessory: UsbAccessory,
    ) {
        if (!UsbAccessoryIdentity.matches(accessory)) {
            return
        }

        if (
            UsbAccessoryConnector.hasPermission(
                usbManager,
                accessory,
            )
        ) {
            usbPermissionRequestInFlight.set(false)
            app.usbRuntimeStatus.publish(
                "USB permission granted. Connecting…",
            )
            usbRuntime.connect(accessory)
            return
        }

        if (!usbPermissionRequestInFlight.compareAndSet(false, true)) {
            return
        }

        app.usbRuntimeStatus.publish(
            "Waiting for Android USB permission…",
        )

        val flags =
            PendingIntent.FLAG_UPDATE_CURRENT or
                if (Build.VERSION.SDK_INT >= 31) {
                    PendingIntent.FLAG_MUTABLE
                } else {
                    0
                }

        val permissionIntent =
            PendingIntent.getBroadcast(
                this,
                0,
                Intent(ACTION_USB_PERMISSION)
                    .setPackage(packageName),
                flags,
            )

        UsbAccessoryConnector.requestPermission(
            usbManager,
            accessory,
            permissionIntent,
        )
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private companion object {
        const val CHANNEL_ID = "controller_connection"
        const val NOTIFICATION_ID = 1001
        const val ACTION_USB_PERMISSION =
            "com.natsx.controller.action.USB_PERMISSION"
        const val USB_ATTACH_POLL_MILLIS = 750L

        val RETRY_BACKOFF_MILLIS =
            longArrayOf(
                1_000,
                2_000,
                4_000,
                8_000,
            )
    }
}
