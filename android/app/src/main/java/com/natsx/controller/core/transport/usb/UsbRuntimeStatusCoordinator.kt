package com.natsx.controller.core.transport.usb

import java.util.concurrent.CopyOnWriteArraySet

data class UsbRuntimeStatus(
    val message: String,
    val isError: Boolean = false,
)

class UsbRuntimeStatusCoordinator {
    private val listeners =
        CopyOnWriteArraySet<(UsbRuntimeStatus) -> Unit>()

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
}
