package com.natsx.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.IBinder
import com.natsx.controller.NatsxControllerApplication
import com.natsx.controller.core.session.ControllerRealtimePublisher
import com.natsx.controller.core.transport.usb.UsbAccessoryConnector
import com.natsx.controller.core.transport.usb.UsbAccessoryIdentity
import com.natsx.controller.core.transport.usb.UsbAccessoryRuntime

class ControllerService : Service() {
    private lateinit var realtimePublisher: ControllerRealtimePublisher
    private lateinit var usbManager: UsbManager
    private lateinit var usbRuntime: UsbAccessoryRuntime

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
                        if (
                            intent.getBooleanExtra(
                                UsbManager.EXTRA_PERMISSION_GRANTED,
                                false,
                            ) &&
                            accessory != null &&
                            UsbAccessoryIdentity.matches(accessory)
                        ) {
                            usbRuntime.connect(accessory)
                        }
                    }

                    UsbManager.ACTION_USB_ACCESSORY_DETACHED -> {
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

        val app = application as NatsxControllerApplication
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
            )

        registerUsbReceiver()
        connectUsbIfPresent()

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
        connectUsbIfPresent()
        return START_STICKY
    }

    override fun onDestroy() {
        if (::usbRuntime.isInitialized) {
            usbRuntime.close()
        }

        runCatching {
            unregisterReceiver(usbReceiver)
        }

        if (::realtimePublisher.isInitialized) {
            realtimePublisher.close()
        }

        super.onDestroy()
    }

    private fun registerUsbReceiver() {
        val filter =
            IntentFilter().apply {
                addAction(ACTION_USB_PERMISSION)
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

    private fun connectUsbIfPresent() {
        val accessory =
            UsbAccessoryConnector.findNatsxAccessory(
                usbManager,
            )
                ?: return

        if (
            UsbAccessoryConnector.hasPermission(
                usbManager,
                accessory,
            )
        ) {
            usbRuntime.connect(accessory)
            return
        }

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
    }
}
