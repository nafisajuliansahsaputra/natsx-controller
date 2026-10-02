package com.natsx.controller.core.connection

import java.util.concurrent.CopyOnWriteArraySet

enum class AndroidLinkState {
    UNAVAILABLE,
    PERMISSION_REQUIRED,
    OFF,
    IDLE,
    CONNECTING,
    ACTIVE,
    RECONNECTING,
    STOPPED,
}

data class AndroidConnectionStatus(
    val wifi: AndroidLinkState = AndroidLinkState.IDLE,
    val bluetooth: AndroidLinkState = AndroidLinkState.IDLE,
    val usb: AndroidLinkState = AndroidLinkState.IDLE,
    val trustedPcCount: Int = 0,
    val wifiReconnectAttempts: Long = 0,
    val bluetoothReconnectAttempts: Long = 0,
) {
    init {
        require(trustedPcCount >= 0)
        require(wifiReconnectAttempts >= 0)
        require(bluetoothReconnectAttempts >= 0)
    }
}

class AndroidConnectionStatusCoordinator {
    private val listeners =
        CopyOnWriteArraySet<(AndroidConnectionStatus) -> Unit>()

    @Volatile
    private var current =
        AndroidConnectionStatus()

    fun current(): AndroidConnectionStatus =
        current

    fun publish(
        next: AndroidConnectionStatus,
    ) {
        if (next == current) {
            return
        }

        current = next

        listeners.forEach { listener ->
            runCatching {
                listener(next)
            }
        }
    }

    fun addListener(
        listener: (AndroidConnectionStatus) -> Unit,
    ) {
        listeners += listener
        listener(current)
    }

    fun removeListener(
        listener: (AndroidConnectionStatus) -> Unit,
    ) {
        listeners -= listener
    }
}
