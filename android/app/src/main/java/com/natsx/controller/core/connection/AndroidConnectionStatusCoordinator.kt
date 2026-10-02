package com.natsx.controller.core.connection

import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.ProtocolTransport
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
    val smartAutoActiveTransport: ProtocolTransport? = null,
    val smartAutoStateSequence: UInt? = null,
) {
    init {
        require(trustedPcCount >= 0)
        require(wifiReconnectAttempts >= 0)
        require(bluetoothReconnectAttempts >= 0)
        require(
            (smartAutoActiveTransport == null) ==
                (smartAutoStateSequence == null),
        ) {
            "Smart Auto authority and state sequence must be published together."
        }
    }
}

class AndroidConnectionStatusCoordinator {
    private val listeners =
        CopyOnWriteArraySet<(AndroidConnectionStatus) -> Unit>()
    private val gate = Any()

    @Volatile
    private var current =
        AndroidConnectionStatus()

    fun current(): AndroidConnectionStatus =
        current

    fun publish(
        next: AndroidConnectionStatus,
    ) {
        notifyIfChanged(
            synchronized(gate) {
                commitLocked(next)
            },
        )
    }

    fun publishLinks(
        next: AndroidConnectionStatus,
    ) {
        val changed =
            synchronized(gate) {
                commitLocked(
                    next.copy(
                        smartAutoActiveTransport =
                            current.smartAutoActiveTransport,
                        smartAutoStateSequence =
                            current.smartAutoStateSequence,
                    ),
                )
            }

        notifyIfChanged(changed)
    }

    fun applyHandoverCommit(
        payload: HandoverPayload,
    ): Boolean {
        val changed =
            synchronized(gate) {
                val previousSequence =
                    current.smartAutoStateSequence

                if (
                    previousSequence != null &&
                    !isNewerSequence(
                        candidate = payload.stateSequence,
                        previous = previousSequence,
                    )
                ) {
                    null
                } else {
                    commitLocked(
                        current.copy(
                            smartAutoActiveTransport =
                                payload.transport,
                            smartAutoStateSequence =
                                payload.stateSequence,
                        ),
                    )
                }
            }

        notifyIfChanged(changed)
        return changed != null
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

    private fun commitLocked(
        next: AndroidConnectionStatus,
    ): AndroidConnectionStatus? {
        if (next == current) {
            return null
        }

        current = next
        return next
    }

    private fun notifyIfChanged(
        changed: AndroidConnectionStatus?,
    ) {
        if (changed == null) {
            return
        }

        listeners.forEach { listener ->
            runCatching {
                listener(changed)
            }
        }
    }

    private fun isNewerSequence(
        candidate: UInt,
        previous: UInt,
    ): Boolean {
        val distance =
            candidate - previous

        return distance != 0u &&
            distance < 0x8000_0000u
    }
}
