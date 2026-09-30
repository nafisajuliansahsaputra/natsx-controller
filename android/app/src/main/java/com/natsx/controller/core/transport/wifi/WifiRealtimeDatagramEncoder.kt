package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion

object WifiRealtimeDatagramEncoder {
    fun encodeGamepadState(
        state: GamepadState,
        trustedSession: WifiTrustedSession,
        sequence: UInt,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.GAMEPAD_STATE,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = trustedSession.sessionId,
            sequence = sequence,
            monotonicTimestampMicros = monotonicTimestampMicros,
            payload = GamepadStateCodec.encode(state),
        )

        return ProtocolFrameCodec.encode(
            frame = frame,
            authenticationKey = trustedSession.authenticationKey(),
        )
    }
}
