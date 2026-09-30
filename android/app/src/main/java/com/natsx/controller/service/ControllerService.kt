package com.natsx.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.IBinder
import com.natsx.controller.NatsxControllerApplication
import com.natsx.controller.core.session.ControllerRealtimePublisher
import com.natsx.controller.core.transport.bluetooth.BluetoothAutoReconnectRuntime
import com.natsx.controller.core.transport.bluetooth.BondedBluetoothRfcommCandidateProvider
import com.natsx.controller.core.transport.bluetooth.TrustedBluetoothRealtimeLinkFactory

class ControllerService : Service() {
    private lateinit var realtimePublisher: ControllerRealtimePublisher
    private var bluetoothReconnectRuntime: BluetoothAutoReconnectRuntime? = null

    override fun onCreate() {
        super.onCreate()

        val app = application as NatsxControllerApplication
        realtimePublisher = ControllerRealtimePublisher(
            stateStore = app.gamepadStateStore,
            broadcaster = app.realtimeBroadcaster,
        )
        realtimePublisher.start()
        startBluetoothRuntimeIfUnambiguous(app)

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
        return START_STICKY
    }

    override fun onDestroy() {
        bluetoothReconnectRuntime?.close()
        bluetoothReconnectRuntime = null

        if (::realtimePublisher.isInitialized) {
            realtimePublisher.close()
        }

        super.onDestroy()
    }

    private fun startBluetoothRuntimeIfUnambiguous(
        app: NatsxControllerApplication,
    ) {
        val trustedPeers =
            app.trustedPeerStore.list()

        if (trustedPeers.size != 1) {
            return
        }

        val receiverPeerId =
            trustedPeers.single().peerId

        val linkFactory =
            TrustedBluetoothRealtimeLinkFactory(
                localPeerId = app.localPeerId,
                receiverPeerId = receiverPeerId,
                trustedPeerStore =
                    app.trustedPeerStore,
                sessionRegistry =
                    app.trustedSessionRegistry,
                candidateProvider =
                    BondedBluetoothRfcommCandidateProvider(
                        this,
                    ),
            )

        bluetoothReconnectRuntime =
            BluetoothAutoReconnectRuntime(
                broadcaster =
                    app.realtimeBroadcaster,
                linkFactory =
                    linkFactory,
            ).also {
                it.start()
            }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private companion object {
        const val CHANNEL_ID = "controller_connection"
        const val NOTIFICATION_ID = 1001
    }
}
