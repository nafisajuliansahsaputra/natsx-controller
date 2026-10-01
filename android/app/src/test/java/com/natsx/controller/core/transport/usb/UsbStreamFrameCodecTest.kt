package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.ByteArrayInputStream

class UsbStreamFrameCodecTest {
    @Test
    fun framedProtocolMessageRoundTrips() {
        val frame =
            ProtocolFrameCodec.encode(
                ProtocolFrame(
                    version = ProtocolVersion.Current,
                    messageType = MessageType.HEARTBEAT,
                    flags = FrameFlags.NONE,
                    sessionId = SessionId.Zero,
                    sequence = 0u,
                    monotonicTimestampMicros = 123uL,
                    payload = byteArrayOf(),
                ),
            )

        val encoded =
            UsbStreamFrameCodec.encode(frame)

        assertEquals(
            frame.size + 2,
            encoded.size,
        )

        assertArrayEquals(
            frame,
            UsbStreamFrameCodec.readFrame(
                ByteArrayInputStream(encoded),
            ),
        )
    }

    @Test(expected = IllegalArgumentException::class)
    fun encoderRejectsOversizedFrame() {
        UsbStreamFrameCodec.encode(
            ByteArray(
                UsbStreamFrameCodec
                    .MAXIMUM_FRAME_SIZE + 1,
            ),
        )
    }
}
