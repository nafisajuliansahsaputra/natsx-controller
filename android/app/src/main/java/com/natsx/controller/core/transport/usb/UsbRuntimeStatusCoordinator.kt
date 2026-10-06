package com.natsx.controller.core.transport.usb

import java.util.concurrent.CopyOnWriteArraySet

data class UsbRuntimeStatus(
    val message: String,
    val isError: Boolean = false,
)

class UsbRuntimeStatusCoordinator {
    private val listeners =
        CopyOnWriteArraySet<(UsbRuntimeStatus) -> Unit>()
    private val historyGate = Any()
    private val history = ArrayDeque<String>()

    @Volatile
    private var current =
        UsbRuntimeStatus(
            "USB idle",
        )

    fun current(): UsbRuntimeStatus =
        current

    fun publish(
        message: String,
        isError: Boolean = false,
    ) {
        val next =
            UsbRuntimeStatus(
                message = message,
                isError = isError,
            )

        current = next

        synchronized(historyGate) {
            history.addLast(
                if (isError) {
                    "ERROR — $message"
                } else {
                    message
                },
            )

            while (history.size > MAX_HISTORY) {
                history.removeFirst()
            }
        }

        listeners.forEach { listener ->
            runCatching {
                listener(next)
            }
        }
    }

    fun addListener(
        listener: (UsbRuntimeStatus) -> Unit,
    ) {
        listeners += listener
        listener(current)
    }

    fun removeListener(
        listener: (UsbRuntimeStatus) -> Unit,
    ) {
        listeners -= listener
    }

    fun historyText(): String =
        synchronized(historyGate) {
            history.joinToString(
                separator = "\n",
            )
        }

    private companion object {
        const val MAX_HISTORY = 8
    }
}
