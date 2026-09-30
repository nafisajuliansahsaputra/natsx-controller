package com.natsx.controller.core.session

import com.natsx.controller.core.gamepad.GamepadState

data class RealtimeStateEnvelope(
    val sequence: UInt,
    val monotonicTimestampMicros: ULong,
    val state: GamepadState,
)

fun interface RealtimeStateSink {
    fun publish(envelope: RealtimeStateEnvelope)
}
