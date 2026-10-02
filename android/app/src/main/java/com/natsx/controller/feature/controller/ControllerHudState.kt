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
