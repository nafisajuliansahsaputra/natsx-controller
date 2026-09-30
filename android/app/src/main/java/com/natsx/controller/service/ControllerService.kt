package com.natsx.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.IBinder
import com.natsx.controller.NatsxControllerApp
import com.natsx.controller.core.connection.ControllerConnectionStatus

class ControllerService : Service() {
    private lateinit var notificationManager: NotificationManager

    override fun onCreate() {
        super.onCreate()

        notificationManager = getSystemService(NotificationManager::class.java)
        notificationManager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "Controller connection",
                NotificationManager.IMPORTANCE_LOW,
            ),
        )

        startForeground(
            NOTIFICATION_ID,
            buildNotification("Starting controller connection…"),
        )

        val runtime =
            (application as NatsxControllerApp).connectionRuntime

        runtime.onStatusChanged = { status ->
            updateNotification(status)
        }

        runtime.start()
    }

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        return START_STICKY
    }

    override fun onDestroy() {
        val runtime =
            (application as NatsxControllerApp).connectionRuntime

        runtime.onStatusChanged = null
        runtime.stop()

        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun updateNotification(status: ControllerConnectionStatus) {
        notificationManager.notify(
            NOTIFICATION_ID,
            buildNotification(status.message),
        )
    }

    private fun buildNotification(message: String): Notification =
        Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle("NATSX Controller")
            .setContentText(message)
            .setOngoing(true)
            .build()

    private companion object {
        const val CHANNEL_ID = "controller_connection"
        const val NOTIFICATION_ID = 1001
    }
}
