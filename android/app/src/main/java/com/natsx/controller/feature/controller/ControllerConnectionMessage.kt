package com.natsx.controller.feature.controller

import com.natsx.controller.core.connection.AndroidConnectionStatus
import com.natsx.controller.core.connection.AndroidLinkState

/** A working touch surface must not look connected before receiver authority is established. */
internal object ControllerConnectionMessage {
    fun forStatus(status: AndroidConnectionStatus): String? = when {
        status.smartAutoActiveTransport != null -> null
        status.usb == AndroidLinkState.PERMISSION_REQUIRED -> "Allow USB access"
        status.trustedPcCount == 0 -> "Pair with receiver"
        status.usb == AndroidLinkState.CONNECTING || status.usb == AndroidLinkState.ACTIVE ||
            status.wifi == AndroidLinkState.CONNECTING || status.wifi == AndroidLinkState.ACTIVE ||
            status.bluetooth == AndroidLinkState.CONNECTING || status.bluetooth == AndroidLinkState.ACTIVE ->
            "Connecting to receiver"
        else -> "Receiver disconnected"
    }
}
