package com.natsx.controller.core.input

data class StickCalibration(
    val centerOffsetX: Float = 0f,
    val centerOffsetY: Float = 0f,
    val travelScale: Float = 1f,
) {
    init {
        require(centerOffsetX in -MAX_CENTER_OFFSET..MAX_CENTER_OFFSET)
        require(centerOffsetY in -MAX_CENTER_OFFSET..MAX_CENTER_OFFSET)
        require(travelScale in MIN_TRAVEL_SCALE..MAX_TRAVEL_SCALE)
    }

    companion object {
        const val MAX_CENTER_OFFSET = 0.20f
        const val MIN_TRAVEL_SCALE = 0.65f
        const val MAX_TRAVEL_SCALE = 1.20f

        val Default = StickCalibration()
    }
}

data class ControllerStickCalibration(
    val left: StickCalibration = StickCalibration.Default,
    val right: StickCalibration = StickCalibration.Default,
) {
    companion object {
        val Default = ControllerStickCalibration()
    }
}
