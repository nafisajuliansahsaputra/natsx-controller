package com.natsx.controller.core.haptics

interface ControllerHapticSink {
    fun touchTick()

    fun applyRumble(
        lowFrequency: Int,
        highFrequency: Int,
        durationMilliseconds: Int = 0,
    )

    fun stopRumble()
}

object NoOpControllerHapticSink : ControllerHapticSink {
    override fun touchTick() = Unit

    override fun applyRumble(
        lowFrequency: Int,
        highFrequency: Int,
        durationMilliseconds: Int,
    ) = Unit

    override fun stopRumble() = Unit
}
