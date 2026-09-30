package com.natsx.controller

import android.app.Application
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.session.ControllerRealtimePublisher
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.session.SessionSequence

class NatsxControllerApplication : Application() {
    val gamepadStateStore: GamepadStateStore by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        GamepadStateStore()
    }

    val sessionSequence: SessionSequence by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        SessionSequence()
    }

    val realtimeBroadcaster: RealtimeStateBroadcaster by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        RealtimeStateBroadcaster(
            sequence = sessionSequence,
            monotonicMicros = ControllerRealtimePublisher::defaultMonotonicMicros,
        )
    }
}
