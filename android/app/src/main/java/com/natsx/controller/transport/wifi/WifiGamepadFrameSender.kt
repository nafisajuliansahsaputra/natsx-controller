package com.natsx.controller.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.connection.GamepadStateSender
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId

class WifiGamepadFrameSender(
    private val transport: WifiControllerTransport,
    private val sessionId: SessionId,
) : GamepadStateSender {
    override fun send(state: GamepadState) {
        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.GAMEPAD_STATE,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = sessionId,
            sequence = transport.nextSequence(),
            monotonicTimestampMicros = (
                SystemClock.elapsedRealtimeNanos() / 1_000L
            ).toULong(),
            payload = GamepadStateCodec.encode(state),
        )

        transport.send(frame)
    }
}
