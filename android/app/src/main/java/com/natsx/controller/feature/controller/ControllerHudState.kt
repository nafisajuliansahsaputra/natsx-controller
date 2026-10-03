package com.natsx.controller.feature.controller

enum class ControllerTransportIndicator {
    WIFI,
    BLUETOOTH,
    USB,
}

data class ControllerHudState(
    val activeTransport: ControllerTransportIndicator? = null,
    val batteryPercent: Int? = null,
    val charging: Boolean = false,
    val message: String? = null,
    val messageIsError: Boolean = false,
) {
    init {
        require(
            batteryPercent == null ||
                batteryPercent in 0..100,
        )
    }
}

internal fun ControllerTransportIndicator?.indicatorColor(): Int = when (this) {
    ControllerTransportIndicator.USB -> 0xFFAA8BE8.toInt()
    ControllerTransportIndicator.WIFI -> 0xFFB2EBB2.toInt()
    ControllerTransportIndicator.BLUETOOTH -> 0xFFF6F6FA.toInt()
    null -> 0xFFB9BBC2.toInt()
}
