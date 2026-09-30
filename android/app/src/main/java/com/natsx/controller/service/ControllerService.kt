package com.natsx.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.IBinder
import com.natsx.controller.NatsxControllerApplication
import com.natsx.controller.core.session.ControllerRealtimePublisher

class ControllerService : Service() {
    private lateinit var realtimePublisher: ControllerRealtimePublisher

    override fun onCreate() {
        super.onCreate()

        val app = application as NatsxControllerApplication
        realtimePublisher = ControllerRealtimePublisher(
            stateStore = app.gamepadStateStore,
            broadcaster = app.realtimeBroadcaster,
        )
        realtimePublisher.start()

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
        if (::realtimePublisher.isInitialized) {
            realtimePublisher.close()
        }

        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private companion object {
        const val CHANNEL_ID = "controller_connection"
        const val NOTIFICATION_ID = 1001
    }
}
