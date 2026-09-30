package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion

object BluetoothRealtimeStreamEncoder {
    fun encodeGamepadState(
        state: GamepadState,
        trustedSession: BluetoothTrustedSession,
        sequence: UInt,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        val frame =
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.GAMEPAD_STATE,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = trustedSession.sessionId,
                sequence = sequence,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload = GamepadStateCodec.encode(state),
            )

        val frameBytes =
            ProtocolFrameCodec.encode(
                frame = frame,
                authenticationKey =
                    trustedSession.authenticationKey(),
            )

        return BluetoothStreamFrameCodec.encode(
            frameBytes,
        )
    }
}
